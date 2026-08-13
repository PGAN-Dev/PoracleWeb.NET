using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/profiles")]
[RequireFeatureEnabled(DisableFeatureKeys.Profiles)]
public class ProfileController(
    IProfileService profileService,
    IHumanService humanService,
    IPoracleHumanProxy humanProxy,
    IProfileRepository profileRepository,
    IJwtService jwtService,
    IUserRoleResolver roleResolver,
    IUserGeofenceRepository userGeofenceRepository) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IProfileRepository _profileRepository = profileRepository;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IUserRoleResolver _roleResolver = roleResolver;
    private readonly IUserGeofenceRepository _userGeofenceRepository = userGeofenceRepository;

    /// <summary>Matches the profiles.name column, so an over-long name is refused rather than 500ing.</summary>
    private const int MaxProfileNameLength = 255;

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

    /// <summary>
    /// Drops any private geofence name from a submitted area list that the caller does not own.
    /// </summary>
    /// <remarks>
    /// Public and admin areas are anyone's to select, so they pass through. What does not is another
    /// user's private fence: PoracleNG's setAreas intersects against userSelectable fences and would
    /// have stripped it, but this path writes the profile row directly. See #647.
    /// </remarks>
    private async Task<string> RemoveAreasTheCallerMayNotSelectAsync(string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return "[]";
        }

        List<string>? requested;
        try
        {
            requested = JsonSerializer.Deserialize<List<string>>(area);
        }
        catch (JsonException ex)
        {
            // Silently emptying a list we could not read is the worst of both: the caller asked for
            // something and gets a 201 saying it worked. See #658.
            throw new ArgumentException("area must be a JSON array of area names.", nameof(area), ex);
        }

        if (requested is null || requested.Count == 0)
        {
            return "[]";
        }

        // Approved fences are excluded: approval promotes them to public Koji areas that anyone may
        // select, and when no promotedName was supplied the public area's name IS the KojiName -- so
        // denying it silently dropped a legitimate public area from the new profile. Only another
        // user's still-private fence is refused. See #658.
        var privateNames = (await this._userGeofenceRepository.GetAllAsync())
            .Where(g => !string.Equals(g.HumanId, this.UserId, StringComparison.OrdinalIgnoreCase))
            .Where(g => !string.Equals(g.Status, "approved", StringComparison.OrdinalIgnoreCase))
            .Select(g => g.KojiName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowed = requested
            .Where(name => !string.IsNullOrWhiteSpace(name) && !privateNames.Contains(name))
            .ToList();

        return JsonSerializer.Serialize(allowed);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return this.BadRequest(new
            {
                error = "Profile name is required."
            });
        }

        // profiles.name is varchar(255); anything longer reached the database and came back as an
        // opaque 500. See #467.
        if (profile.Name.Trim().Length > MaxProfileNameLength)
        {
            return this.BadRequest(new
            {
                error = $"Profile name must be {MaxProfileNameLength} characters or fewer."
            });
        }

        profile.Id = this.UserId;

        var (isValid, validationError) = ActiveHoursValidator.Validate(profile.ActiveHours);
        if (!isValid)
        {
            return this.BadRequest(validationError);
        }

        // The request supplies its own geography, and nothing bounded it. Coordinates out of range reach
        // the active-hours scheduler, which reads them as a timezone; an area list could name another
        // user's private geofence, which PoracleNG's own setAreas filter would have stripped but a direct
        // write does not. See #647.
        //
        // Both checks run BEFORE the profile is created. Behind it, a refusal answered 400 while leaving
        // a profile PoracleNG had already made -- and since addProfile ignores `area`, that orphan came
        // up carrying the ACTIVE profile's entire area list and location, the inheritance #563 exists to
        // prevent. A retry then made a second one. See #665.
        if (profile.Latitude is < -90 or > 90 || profile.Longitude is < -180 or > 180)
        {
            return this.BadRequest(new
            {
                error = "Latitude must be between -90 and 90, and longitude between -180 and 180.",
            });
        }

        string requestedAreas;
        try
        {
            requestedAreas = await this.RemoveAreasTheCallerMayNotSelectAsync(profile.Area);
        }
        catch (ArgumentException ex)
        {
            return this.BadRequest(new { error = ex.Message });
        }

        // PoracleNG assigns the lowest free number, not max+1, so the number cannot be predicted for a
        // user who has ever deleted a non-last profile. Ask what it chose. See #407.
        var before = (await this._profileService.GetByUserAsync(this.UserId)).ToList();

        var body = JsonSerializer.SerializeToElement(new
        {
            name = profile.Name,
            area = requestedAreas,
            latitude = profile.Latitude,
            longitude = profile.Longitude,
            active_hours = profile.ActiveHours
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        var after = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var createdNo = ProfileNumbering.ResolveCreated(before, after, profile.Name);
        if (createdNo is null)
        {
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The profile was not created."
            });
        }

        // addProfile ignores area, latitude and longitude, so a new profile came up carrying whatever the
        // ACTIVE profile had -- every area subscription the user held, and a location that also drives the
        // active-hours timezone. Duplicate and import already write the geography directly after creating;
        // create needs the same. Empty unless the request asked for something. See #563.
        try
        {
            await this._profileRepository.UpdateAsync(new Profile
            {
                Id = this.UserId,
                ProfileNo = createdNo.Value,
                Name = profile.Name.Trim(),
                Area = requestedAreas,
                Latitude = profile.Latitude,
                Longitude = profile.Longitude,
            });
        }
        catch (InvalidOperationException)
        {
            // The row is not visible yet in some PoracleNG timings. The profile exists either way, and a
            // wrong area list is fixable from the Areas page; a failed create is not.
        }

        var result = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, createdNo.Value);
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

        if (profile.Name is not null && profile.Name.Trim().Length > MaxProfileNameLength)
        {
            return this.BadRequest(new
            {
                error = $"Profile name must be {MaxProfileNameLength} characters or fewer."
            });
        }

        var (isValid, validationError) = ActiveHoursValidator.Validate(profile.ActiveHours);
        if (!isValid)
        {
            return this.BadRequest(validationError);
        }

        var body = JsonSerializer.SerializeToElement(new
        {
            profile_no = profileNo,
            name = profile.Name ?? existing.Name,
            active_hours = profile.ActiveHours ?? existing.ActiveHours
        });
        await this._humanProxy.UpdateProfileAsync(this.UserId, body);

        // PoracleNG's update handler answers ok and silently drops the name, while honouring active_hours
        // on the very same request -- so rename has to be written directly. The response used to be
        // re-read and returned as a 200 carrying the OLD name, and the SPA built a success toast from it.
        // See #406.
        var newName = profile.Name?.Trim();
        if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, existing.Name, StringComparison.Ordinal))
        {
            var renamed = await this._profileRepository.RenameAsync(this.UserId, profileNo, newName);
            if (!renamed)
            {
                return this.NotFound();
            }
        }

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
        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, profileNo, resolvedAdmin);

        return this.Ok(new
        {
            profile,
            token = newToken
        });
    }

    [HttpPost("duplicate")]
    public async Task<IActionResult> Duplicate([FromBody] DuplicateProfileRequest request)
    {
        // Duplicate skipped the length check create has had since #467, so a long name reached the
        // varchar(255) column and came back as an opaque 500. See #519.
        var nameError = ProfileNameRules.Validate(request.Name);
        if (nameError is not null)
        {
            return this.BadRequest(new { error = nameError });
        }

        var sourceProfile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, request.FromProfileNo);
        if (sourceProfile == null)
        {
            return this.NotFound();
        }

        // See #407: PoracleNG picks the number, so create first and then ask which one it used. Copying
        // to a predicted max+1 wrote the alarms to a profile_no with no profile row, and those orphans
        // later attached themselves to whatever profile was eventually created at that number.
        var before = (await this._profileService.GetByUserAsync(this.UserId)).ToList();

        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name.Trim(),
            area = sourceProfile.Area ?? "[]",
            latitude = sourceProfile.Latitude,
            longitude = sourceProfile.Longitude,
            active_hours = sourceProfile.ActiveHours
        });
        await this._humanProxy.AddProfileAsync(this.UserId, body);

        var after = (await this._profileService.GetByUserAsync(this.UserId)).ToList();
        var resolved = ProfileNumbering.ResolveCreated(before, after, request.Name.Trim());
        if (resolved is null)
        {
            return this.StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "The profile was not created."
            });
        }

        var newProfileNo = resolved.Value;

        // PoracleNG's addProfile ignores area, latitude and longitude while honouring active_hours
        // from the same payload, so a duplicate silently inherited the ACTIVE profile's geography
        // instead of the source's: the right alarms over the wrong map, and a location that also
        // feeds the active-hours timezone. Write them directly, the same way rename has to (#406).
        // See #466.
        try
        {
            await this._profileRepository.UpdateAsync(new Profile
            {
                Id = this.UserId,
                ProfileNo = newProfileNo,
                Name = request.Name.Trim(),
                Area = sourceProfile.Area ?? "[]",
                Latitude = sourceProfile.Latitude,
                Longitude = sourceProfile.Longitude,
            });
        }
        catch (InvalidOperationException)
        {
            // The row is not there yet in some PoracleNG timings; the profile still exists and the
            // alarms still copy, so this must not fail the duplicate.
        }

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
}

public class DuplicateProfileRequest
{
    public int FromProfileNo
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
}
