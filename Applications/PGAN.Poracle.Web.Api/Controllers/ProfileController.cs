using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/profiles")]
public class ProfileController(IProfileService profileService, IHumanService humanService) : BaseApiController
{
    private readonly IProfileService _profileService = profileService;
    private readonly IHumanService _humanService = humanService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var profiles = await this._profileService.GetByUserAsync(this.UserId);
        return this.Ok(profiles);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Profile profile)
    {
        profile.Id = this.UserId;
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
        await this._humanService.UpdateAsync(human);

        return this.Ok(profile);
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
}
