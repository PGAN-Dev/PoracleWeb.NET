using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/profile-overview")]
[RequireFeatureEnabled(DisableFeatureKeys.Profiles)]
public partial class ProfileOverviewController(
    IProfileOverviewService profileOverviewService,
    IProfileService profileService,
    IPoracleHumanProxy humanProxy,
    IJwtService jwtService,
    ILogger<ProfileOverviewController> logger) : BaseApiController
{
    private readonly IProfileOverviewService _profileOverviewService = profileOverviewService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IProfileService _profileService = profileService;
    private readonly ILogger<ProfileOverviewController> _logger = logger;

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

        // PoracleNG assigns the lowest free number, not max+1, so it cannot be predicted. Create, then
        // ask which number it used. See #407.
        var before = (await this._profileService.GetByUserAsync(this.UserId)).ToList();

        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name,
            area = source.Area ?? "[]",
            latitude = source.Latitude,
            longitude = source.Longitude,
            active_hours = source.ActiveHours
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        var after = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var resolved = ProfileNumbering.ResolveCreated(before, after, request.Name);
        if (resolved is null)
        {
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The profile was not created."
            });
        }

        var newProfileNo = resolved.Value;

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
        var existing = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
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
            area = "[]",
            latitude = 0.0,
            longitude = 0.0
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        var after = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var resolved = ProfileNumbering.ResolveCreated(existing, after, profileName);
        if (resolved is null)
        {
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The profile was not created."
            });
        }

        var newProfileNo = resolved.Value;

        // A malformed payload -- including "alarms": null, which the SPA's typeof-object guard lets
        // through from the file picker -- used to throw here with the profile row already created,
        // leaving a junk profile behind per failed attempt. See #407.
        int alarmsCopied;
        try
        {
            alarmsCopied = await this._profileOverviewService.ImportAlarmsAsync(
                this.UserId, newProfileNo, request.Alarms);
        }
        catch (FeatureDisabledException)
        {
            // Roll back the shell profile, but let this reach the global filter so it still maps to a
            // 403 with a disableKey rather than being flattened into a generic 400. See #236.
            await this._humanProxy.DeleteProfileAsync(this.UserId, newProfileNo);
            throw;
        }
        catch (Exception ex)
        {
            await this._humanProxy.DeleteProfileAsync(this.UserId, newProfileNo);
            LogImportFailed(this._logger, ex, newProfileNo);
            return this.BadRequest(new
            {
                error = "The profile could not be imported. Check that the file contains a valid alarms list."
            });
        }

        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, this.ProfileNo);

        return this.Ok(new
        {
            alarmsCopied,
            newProfileNo,
            token = newToken
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile import failed for profile {ProfileNo}; the partially created profile was rolled back")]
    private static partial void LogImportFailed(ILogger logger, Exception ex, int profileNo);
}

public record ProfileOverviewDuplicateRequest(string Name);
public record ProfileOverviewImportRequest(string ProfileName, int Version, JsonElement Alarms);
