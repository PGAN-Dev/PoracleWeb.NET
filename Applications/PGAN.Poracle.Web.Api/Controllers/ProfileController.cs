using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/profiles")]
public class ProfileController : BaseApiController
{
    private readonly IProfileService _profileService;
    private readonly IHumanService _humanService;

    public ProfileController(IProfileService profileService, IHumanService humanService)
    {
        _profileService = profileService;
        _humanService = humanService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await _profileService.GetByUserAsync(UserId);
        return Ok(profiles);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Profile profile)
    {
        profile.Id = UserId;
        var result = await _profileService.CreateAsync(profile);
        return CreatedAtAction(nameof(GetAll), result);
    }

    [HttpPut("{profileNo:int}")]
    public async Task<IActionResult> Update(int profileNo, [FromBody] Profile profile)
    {
        var existing = await _profileService.GetByUserAndProfileNoAsync(UserId, profileNo);
        if (existing == null)
            return NotFound();

        existing.Name = profile.Name;
        var result = await _profileService.UpdateAsync(existing);
        return Ok(result);
    }

    [HttpPut("switch/{profileNo:int}")]
    public async Task<IActionResult> SwitchProfile(int profileNo)
    {
        var profile = await _profileService.GetByUserAndProfileNoAsync(UserId, profileNo);
        if (profile == null)
            return NotFound();

        var human = await _humanService.GetByIdAsync(UserId);
        if (human == null)
            return NotFound();

        human.CurrentProfileNo = profileNo;
        await _humanService.UpdateAsync(human);

        return Ok(profile);
    }

    [HttpDelete("{profileNo:int}")]
    public async Task<IActionResult> Delete(int profileNo)
    {
        var success = await _profileService.DeleteAsync(UserId, profileNo);
        if (!success)
            return NotFound();
        return NoContent();
    }
}
