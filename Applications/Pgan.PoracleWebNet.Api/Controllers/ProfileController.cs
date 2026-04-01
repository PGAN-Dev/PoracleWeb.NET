using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/profiles")]
public class ProfileController(
    IProfileService profileService,
    IHumanService humanService,
    IOptions<JwtSettings> jwtSettings) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var profiles = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var human = await this._humanService.GetByIdAsync(this.UserId);
        var activeNo = human?.CurrentProfileNo ?? 1;

        foreach (var p in profiles)
        {
            p.Active = p.ProfileNo == activeNo;
        }

        return this.Ok(profiles);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Profile profile)
    {
        profile.Id = this.UserId;

        // Assign next available profile number
        var existing = await this._profileService.GetByUserAsync(this.UserId);
        var maxNo = existing.Any() ? existing.Max(p => p.ProfileNo) : 0;
        profile.ProfileNo = maxNo + 1;

        var result = await this._profileService.CreateAsync(profile);
        return this.CreatedAtAction(nameof(GetAll), result);
    }

    [HttpPut("{profileNo:int}")]
    public async Task<IActionResult> Update(int profileNo, [FromBody] Profile profile)
    {
        var existing = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
        if (existing == null)
        {
            return this.NotFound();
        }

        existing.Name = profile.Name;
        var result = await this._profileService.UpdateAsync(existing);
        return this.Ok(result);
    }

    [HttpPut("switch/{profileNo:int}")]
    public async Task<IActionResult> SwitchProfile(int profileNo)
    {
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
        if (profile == null)
        {
            return this.NotFound();
        }

        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        // HACK: Two separate SaveChangesAsync calls — not wrapped in a transaction.
        // If the second write fails after the first succeeds, humans.area and profiles.area
        // become inconsistent. PoracleNG's switchProfile endpoint handles this atomically.
        // TODO: Replace with single PoracleNG API call: POST /api/humans/{id}/switchProfile/{profile}
        // See: docs/poracleng-enhancement-requests.md#atomic-profile-switch

        // Save current humans.area to the old profile's profiles.area
        var oldProfile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (oldProfile != null)
        {
            oldProfile.Area = human.Area ?? "[]";
            await this._profileService.UpdateAsync(oldProfile);
        }

        // Load new profile's areas into humans.area
        human.CurrentProfileNo = profileNo;
        human.Latitude = profile.Latitude;
        human.Longitude = profile.Longitude;
        human.Area = profile.Area ?? "[]";
        await this._humanService.UpdateAsync(human);

        // Issue a new JWT with the updated profileNo so all subsequent API calls use it
        var newToken = this.GenerateTokenWithProfile(profileNo);

        return this.Ok(new
        {
            profile,
            token = newToken
        });
    }

    // HACK: Profile deletion does not cascade-delete alarms scoped to (id, profile_no), does not
    // reassign humans.current_profile_no if the active profile is deleted, and does not remove
    // the profile's areas from humans.area. This leaves orphaned alarm records in the DB.
    // PoracleNG's DELETE /api/profiles/{id}/byProfileNo/{n} likely handles cascade cleanup.
    // TODO: Replace with PoracleNG API call once confirmed it cascades.
    // See: docs/poracleng-enhancement-requests.md#profile-delete-cascade
    [HttpDelete("{profileNo:int}")]
    public async Task<IActionResult> Delete(int profileNo)
    {
        var success = await this._profileService.DeleteAsync(this.UserId, profileNo);
        if (!success)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    private string GenerateTokenWithProfile(int profileNo)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Copy all existing claims but replace profileNo
        var claims = new List<Claim>();
        foreach (var claim in this.User.Claims)
        {
            if (claim.Type == "profileNo")
            {
                continue;
            }

            claims.Add(new Claim(claim.Type, claim.Value));
        }
        claims.Add(new Claim("profileNo", profileNo.ToString(CultureInfo.InvariantCulture)));

        var token = new JwtSecurityToken(
            issuer: this._jwtSettings.Issuer,
            audience: this._jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
