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
    IJwtService jwtService,
    IUserRoleResolver roleResolver,
    IUserAreaWriter areaWriter,
    ILogger<ProfileController> logger) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IUserRoleResolver _roleResolver = roleResolver;
    private readonly IUserAreaWriter _areaWriter = areaWriter;
    private readonly ILogger<ProfileController> _logger = logger;

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
    /// Drops any name from a submitted area list that the caller may not subscribe to.
    /// </summary>
    /// <remarks>
    /// Another user's private fence, a non-selectable admin fence and an out-of-community fence all fall
    /// outside <see cref="IUserAreaWriter.FilterAllowedAsync"/>. The new profile's areas are written
    /// trusted, so this is the only check. See #647.
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

        // The same rule a save applies: PoracleNG's selectable areas for this user, their own fences, and
        // what their active profile already holds. The geography write below is trusted, so anything not
        // allowed here would otherwise reach a fence area_security or userSelectable=false is meant to deny.
        var allowed = await this._areaWriter.FilterAllowedAsync(
            this.UserId, requested.Where(name => !string.IsNullOrWhiteSpace(name)).ToList());

        return JsonSerializer.Serialize(allowed);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Profile profile)
    {
        // Required, no longer than the varchar(255) column (#467), and free of control and text-direction
        // characters -- the same rule duplicate, overview-duplicate and import apply.
        var nameError = ProfileNameRules.Validate(profile.Name);
        if (nameError is not null || profile.Name is null)
        {
            return this.BadRequest(new
            {
                error = nameError ?? "Profile name is required."
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

        // Trimmed here: the add stores the name that stays, and the geography write below carries none.
        var name = profile.Name.Trim();

        var body = JsonSerializer.SerializeToElement(new
        {
            name,
            area = requestedAreas,
            latitude = profile.Latitude,
            longitude = profile.Longitude,
            active_hours = profile.ActiveHours
        });

        // PoracleNG assigns the lowest free number, not max+1, and reports it; AddProfileAsync throws when
        // it does not. See #407.
        var createdNo = await this._humanProxy.AddProfileAsync(this.UserId, body);

        // addProfile ignores area, latitude and longitude, so a new profile came up carrying whatever the
        // ACTIVE profile had -- every area subscription the user held, and a location that also drives the
        // active-hours timezone. Duplicate and import already write the geography after creating; create
        // needs the same. Empty unless the request asked for something. See #563. Through PoracleNG's v2
        // ?profile= writes (#993).
        await NewProfileGeography.WriteOrRollBackAsync(
            this._humanProxy, this._logger, this.UserId, createdNo, requestedAreas, profile.Latitude, profile.Longitude);

        var result = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, createdNo);
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

        // Blank means "not renaming". Only a name that
        // actually changes is judged: one stored before a rule existed must not fail every later edit of
        // the profile, and the active-hours editor resubmits the name it was given.
        var newName = string.IsNullOrWhiteSpace(profile.Name) ? null : profile.Name.Trim();
        if (newName is not null && !string.Equals(newName, existing.Name, StringComparison.Ordinal))
        {
            var nameError = ProfileNameRules.Validate(newName);
            if (nameError is not null)
            {
                return this.BadRequest(new
                {
                    error = nameError
                });
            }
        }

        var (isValid, validationError) = ActiveHoursValidator.Validate(profile.ActiveHours);
        if (!isValid)
        {
            return this.BadRequest(validationError);
        }

        // Trimmed, and the stored name when none was given: the PATCH stores exactly what it is sent, so
        // untrimmed it kept the padding and a blank one blanked the name. The stored schedule is not
        // resent: a null active_hours is omitted and v2 leaves the schedule unchanged, so a profile whose
        // stored schedule v2 would refuse can still be renamed.
        var body = JsonSerializer.SerializeToElement(new
        {
            profile_no = profileNo,
            name = newName ?? existing.Name,
            active_hours = profile.ActiveHours
        });
        await this._humanProxy.UpdateProfileAsync(this.UserId, body);

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

        var body = JsonSerializer.SerializeToElement(new
        {
            name = request.Name.Trim(),
            area = sourceProfile.Area ?? "[]",
            latitude = sourceProfile.Latitude,
            longitude = sourceProfile.Longitude,
            active_hours = sourceProfile.ActiveHours
        });
        // See #407: PoracleNG picks the number and reports it. Copying to a predicted max+1 wrote the
        // alarms to a profile_no with no profile row.
        var newProfileNo = await this._humanProxy.AddProfileAsync(this.UserId, body);

        // PoracleNG's addProfile ignores area, latitude and longitude while honouring active_hours
        // from the same payload, so a duplicate silently inherited the ACTIVE profile's geography
        // instead of the source's: the right alarms over the wrong map, and a location that also
        // feeds the active-hours timezone. See #466. Through PoracleNG's v2 ?profile= writes (#993).
        // Geography and the alarm copy share one rollback: a failure of either removes the new profile.
        try
        {
            await NewProfileGeography.WriteAsync(
                this._humanProxy, this.UserId, newProfileNo, sourceProfile.Area ?? "[]",
                sourceProfile.Latitude, sourceProfile.Longitude);

            // Copy all alarms from source to new profile
            await this._profileService.CopyAsync(this.UserId, request.FromProfileNo, newProfileNo);
        }
        catch
        {
            // Roll back the empty profile so the user doesn't end up with a shell
            await NewProfileGeography.RollBackAsync(this._humanProxy, this._logger, this.UserId, newProfileNo);
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
