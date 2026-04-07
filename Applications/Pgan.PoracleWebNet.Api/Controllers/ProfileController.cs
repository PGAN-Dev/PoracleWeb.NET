using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
    IPoracleHumanProxy humanProxy,
    IOptions<JwtSettings> jwtSettings) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
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

        var body = JsonSerializer.SerializeToElement(new
        {
            name = profile.Name,
            profileNo = profile.ProfileNo,
            area = profile.Area ?? "[]",
            latitude = profile.Latitude,
            longitude = profile.Longitude
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        // Re-read the created profile from the DB so we return the full model
        var result = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profile.ProfileNo);
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

        var body = JsonSerializer.SerializeToElement(new
        {
            profile_no = profileNo,
            name = profile.Name
        });
        await this._humanProxy.UpdateProfileAsync(this.UserId, body);

        // Re-read from proxy to return the updated model
        var result = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
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

        // PoracleNG handles the area save/load dual-write atomically:
        // saves current humans.area → old profiles.area, loads new profiles.area → humans.area,
        // and updates humans.current_profile_no + lat/lon in a single operation.
        await this._humanProxy.SwitchProfileAsync(this.UserId, profileNo);

        // Issue a new JWT with the updated profileNo so all subsequent API calls use it
        var newToken = this.GenerateTokenWithProfile(profileNo);

        return this.Ok(new
        {
            profile,
            token = newToken
        });
    }

    [HttpPost("duplicate")]
    public async Task<IActionResult> Duplicate([FromBody] DuplicateProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return this.BadRequest("Profile name is required.");
        }

        var sourceProfile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, request.FromProfileNo);
        if (sourceProfile == null)
        {
            return this.NotFound();
        }

        // Assign next available profile number
        var existing = await this._profileService.GetByUserAsync(this.UserId);
        var newProfileNo = existing.Any() ? existing.Max(p => p.ProfileNo) + 1 : 1;

        // Create the new profile
        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name.Trim(),
            profileNo = newProfileNo,
            area = sourceProfile.Area ?? "[]",
            latitude = sourceProfile.Latitude,
            longitude = sourceProfile.Longitude
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        // Copy all alarms from source to new profile; clean up on failure
        try
        {
            await this._profileService.CopyAsync(this.UserId, request.FromProfileNo, newProfileNo);
        }
        catch
        {
            // Roll back the empty profile so the user doesn't end up with a shell
            await this._humanProxy.DeleteProfileAsync(this.UserId, newProfileNo);
            throw;
        }

        var result = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, newProfileNo);
        return this.CreatedAtAction(nameof(GetAll), result);
    }

    [HttpDelete("{profileNo:int}")]
    public async Task<IActionResult> Delete(int profileNo)
    {
        var existing = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
        if (existing == null)
        {
            return this.NotFound();
        }

        // PoracleNG cascade-deletes alarms scoped to (id, profile_no) and
        // reassigns humans.current_profile_no if the active profile is deleted.
        await this._humanProxy.DeleteProfileAsync(this.UserId, profileNo);

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

public class DuplicateProfileRequest
{
    public int FromProfileNo
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
}
