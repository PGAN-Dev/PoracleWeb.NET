using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PGAN.Poracle.Web.Api.Configuration;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

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

        human.CurrentProfileNo = profileNo;
        human.Latitude = profile.Latitude;
        human.Longitude = profile.Longitude;
        await this._humanService.UpdateAsync(human);

        // Issue a new JWT with the updated profileNo so all subsequent API calls use it
        var newToken = this.GenerateTokenWithProfile(profileNo);

        return this.Ok(new { profile, token = newToken });
    }

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
            if (claim.Type == "profileNo") continue;
            claims.Add(new Claim(claim.Type, claim.Value));
        }
        claims.Add(new Claim("profileNo", profileNo.ToString()));

        var token = new JwtSecurityToken(
            issuer: this._jwtSettings.Issuer,
            audience: this._jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(this._jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
