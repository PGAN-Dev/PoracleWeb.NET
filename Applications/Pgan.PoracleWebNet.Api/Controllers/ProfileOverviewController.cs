using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/profile-overview")]
public class ProfileOverviewController(
    IProfileOverviewService profileOverviewService,
    IProfileService profileService,
    IPoracleHumanProxy humanProxy,
    IJwtService jwtService) : BaseApiController
{
    private readonly IProfileOverviewService _profileOverviewService = profileOverviewService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IProfileService _profileService = profileService;

    [HttpGet]
    public async Task<IActionResult> GetAllProfilesOverview()
    {
        var overview = await this._profileOverviewService.GetAllProfilesOverviewAsync(this.UserId);
        return this.Ok(overview);
    }

    [HttpPost("duplicate/{profileNo:int}")]
    public async Task<IActionResult> DuplicateProfile(int profileNo, [FromBody] ProfileOverviewDuplicateRequest request)
    {
        // Verify source profile exists
        var source = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
        if (source == null)
        {
            return this.NotFound();
        }

        // Create the new profile with next available number
        var existing = await this._profileService.GetByUserAsync(this.UserId);
        var maxNo = existing.Any() ? existing.Max(p => p.ProfileNo) : 0;
        var newProfileNo = maxNo + 1;

        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name,
            profileNo = newProfileNo,
            area = source.Area ?? "[]",
            latitude = source.Latitude,
            longitude = source.Longitude,
            active_hours = source.ActiveHours
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        // Copy all alarms from source to new profile; roll back on failure
        int alarmsCopied;
        try
        {
            alarmsCopied = await this._profileOverviewService.DuplicateProfileAsync(
                this.UserId, profileNo, newProfileNo);
        }
        catch
        {
            try
            {
                await this._humanProxy.DeleteProfileAsync(this.UserId, newProfileNo);
            }
            catch
            {
                // Rollback failed — log but don't mask the original error
            }
            throw;
        }

        // Issue a new JWT so the current profile stays correct
        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, this.ProfileNo);

        return this.Ok(new
        {
            alarmsCopied,
            newProfileNo,
            token = newToken
        });
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportProfile([FromBody] ProfileOverviewImportRequest request)
    {
        // Create a new profile with next available number and unique name
        var existing = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var maxNo = existing.Count > 0 ? existing.Max(p => p.ProfileNo) : 0;
        var newProfileNo = maxNo + 1;

        var existingNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profileName = request.ProfileName;
        if (existingNames.Contains(profileName))
        {
            var suffix = 2;
            while (existingNames.Contains($"{request.ProfileName} ({suffix})"))
            {
                suffix++;
            }

            profileName = $"{request.ProfileName} ({suffix})";
        }

        var body = JsonSerializer.SerializeToElement(new
        {
            name = profileName,
            profileNo = newProfileNo,
            area = "[]",
            latitude = 0.0,
            longitude = 0.0
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        // Import all alarms into the new profile
        var alarmsCopied = await this._profileOverviewService.ImportAlarmsAsync(
            this.UserId, newProfileNo, request.Alarms);

        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, this.ProfileNo);

        return this.Ok(new
        {
            alarmsCopied,
            newProfileNo,
            token = newToken
        });
    }

}

public record ProfileOverviewDuplicateRequest(string Name);
public record ProfileOverviewImportRequest(string ProfileName, int Version, JsonElement Alarms);
