using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Configuration;
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
    IJwtService jwtService) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IProfileRepository _profileRepository = profileRepository;
    private readonly IJwtService _jwtService = jwtService;

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
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return this.BadRequest(new
            {
                error = "Profile name is required."
            });
        }

        profile.Id = this.UserId;

        var (isValid, validationError) = ActiveHoursValidator.Validate(profile.ActiveHours);
        if (!isValid)
        {
            return this.BadRequest(validationError);
        }

        // PoracleNG assigns the lowest free number, not max+1, so the number cannot be predicted for a
        // user who has ever deleted a non-last profile. Ask what it chose. See #407.
        var before = (await this._profileService.GetByUserAsync(this.UserId)).ToList();

        var body = JsonSerializer.SerializeToElement(new
        {
            name = profile.Name,
            area = profile.Area ?? "[]",
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
        var newToken = this._jwtService.GenerateTokenWithReplacedProfile(this.User, profileNo);

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
