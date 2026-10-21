using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
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
    IUserRoleResolver roleResolver,
    ILogger<ProfileOverviewController> logger) : BaseApiController
{
    private readonly IProfileOverviewService _profileOverviewService = profileOverviewService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IUserRoleResolver _roleResolver = roleResolver;
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
        // This endpoint had no name check at all, so an empty or over-long name reached the varchar(255)
        // column and came back as an opaque 500 -- and this page prompts for the name with a free-text
        // input that has no maxlength, prefilled "<source> (Copy)". See #504, #519.
        var nameError = ProfileNameRules.Validate(request.Name);
        if (nameError is not null)
        {
            return this.BadRequest(new { error = nameError });
        }

        // Verify source profile exists
        var source = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, profileNo);
        if (source == null)
        {
            return this.NotFound();
        }

        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name.Trim(),
            area = source.Area ?? "[]",
            latitude = source.Latitude,
            longitude = source.Longitude,
            active_hours = source.ActiveHours
        });
        // PoracleNG assigns the lowest free number, not max+1, and reports it. See #407.
        var newProfileNo = await this._humanProxy.AddProfileAsync(this.UserId, body);

        // Copy all alarms from source to new profile; roll back on failure
        int alarmsCopied;
        try
        {
            alarmsCopied = await this._profileOverviewService.DuplicateProfileAsync(
                this.UserId, profileNo, newProfileNo);

            // PoracleNG's addProfile ignores area, latitude and longitude, so the copy would inherit the
            // active profile's: the right alarms over the wrong map, and a location that also drives the
            // active-hours timezone (#466, #503). Written after the alarm copy, and inside the same rollback.
            await this.WriteGeographyAsync(newProfileNo, source.Area ?? "[]", source.Latitude, source.Longitude);
        }
        catch
        {
            // A rollback failure is logged, not thrown, so the original error is the one reported
            await NewProfileGeography.RollBackAsync(this._humanProxy, this._logger, this.UserId, newProfileNo);
            throw;
        }

        // Issue a new JWT so the current profile stays correct
        // Resolved fresh, not copied from the old token: a profile switch was the way a de-admined
        // user kept their isAdmin claim alive indefinitely. See #624.
        var roles = await this._roleResolver.ResolveAsync(this.UserId);

        // Null means "leave the claim alone". Two cases need it: the resolver could not reach PoracleNG,
        // where treating unknown as false stripped admin for the rest of the session (#656); and an
        // impersonation session, which AdminController deliberately mints with IsAdmin = false and which
        // would otherwise be re-elevated by resolving the impersonated user's own roles (#663).
        bool? resolvedAdmin = roles.Resolved && !this.IsImpersonating
            ? roles.IsAdmin
            : null;
        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, this.ProfileNo, resolvedAdmin);

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
        // Import used to 500 on a blank or over-long name, where create answers a clean 400. See #467.
        var nameError = ProfileNameRules.Validate(request.ProfileName);
        if (nameError is not null)
        {
            return this.BadRequest(new { error = nameError });
        }

        var existing = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var existingNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedName = request.ProfileName.Trim();
        var profileName = requestedName;
        if (existingNames.Contains(profileName))
        {
            var suffix = 2;
            while (existingNames.Contains($"{requestedName} ({suffix})"))
            {
                suffix++;
            }

            profileName = $"{requestedName} ({suffix})";
        }

        var body = JsonSerializer.SerializeToElement(new
        {
            name = profileName,
            area = "[]",
            latitude = 0.0,
            longitude = 0.0
        });
        var newProfileNo = await this._humanProxy.AddProfileAsync(this.UserId, body);

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
        catch (AlarmValidationException)
        {
            // Roll back the shell profile, then let it reach the global filter: the message names the
            // alarm and the field, which is a great deal more use than "check that the file is valid".
            // See #548.
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

        // The shell profile is created empty on purpose, but copying the alarms switches PoracleNG onto
        // it and back, and that switch carries the active profile's areas and location across -- so an
        // import ended up subscribed to every area the user currently had, delivering notifications they
        // never chose for it. Restore what the file actually declared. See #522.
        try
        {
            await this.WriteGeographyAsync(newProfileNo, "[]", 0.0, 0.0);
        }
        catch
        {
            await NewProfileGeography.RollBackAsync(this._humanProxy, this._logger, this.UserId, newProfileNo);
            throw;
        }

        // Resolved fresh, not copied from the old token: a profile switch was the way a de-admined
        // user kept their isAdmin claim alive indefinitely. See #624.
        var roles = await this._roleResolver.ResolveAsync(this.UserId);

        // Null means "leave the claim alone". Two cases need it: the resolver could not reach PoracleNG,
        // where treating unknown as false stripped admin for the rest of the session (#656); and an
        // impersonation session, which AdminController deliberately mints with IsAdmin = false and which
        // would otherwise be re-elevated by resolving the impersonated user's own roles (#663).
        bool? resolvedAdmin = roles.Resolved && !this.IsImpersonating
            ? roles.IsAdmin
            : null;
        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, this.ProfileNo, resolvedAdmin);

        return this.Ok(new
        {
            alarmsCopied,
            newProfileNo,
            token = newToken
        });
    }

    /// <summary>
    /// Sets a new profile's areas and location, which addProfile ignores, through PoracleNG's v2
    /// <c>?profile=</c> writes. A failure propagates. See <see cref="NewProfileGeography"/> and #993.
    /// </summary>
    private Task WriteGeographyAsync(int profileNo, string area, double latitude, double longitude) =>
        NewProfileGeography.WriteAsync(this._humanProxy, this.UserId, profileNo, area, latitude, longitude);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile import failed for profile {ProfileNo}; the partially created profile was rolled back")]
    private static partial void LogImportFailed(ILogger logger, Exception ex, int profileNo);
}

public record ProfileOverviewDuplicateRequest(string Name);
public record ProfileOverviewImportRequest(string ProfileName, int Version, JsonElement Alarms);
