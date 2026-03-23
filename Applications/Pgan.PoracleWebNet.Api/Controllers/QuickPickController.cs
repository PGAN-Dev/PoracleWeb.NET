using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/quick-picks")]
public class QuickPickController(IQuickPickService quickPickService) : BaseApiController
{
    private readonly IQuickPickService _quickPickService = quickPickService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var picks = await this._quickPickService.GetAllAsync(this.UserId, this.ProfileNo);
        return this.Ok(picks);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var pick = await this._quickPickService.GetByIdAsync(id);
        if (pick is null)
        {
            return this.NotFound();
        }

        return this.Ok(pick);
    }

    [HttpPost]
    public async Task<IActionResult> SaveAdmin([FromBody] QuickPickDefinition definition)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var saved = await this._quickPickService.SaveAdminPickAsync(definition);
        return this.Ok(saved);
    }

    [HttpPost("user")]
    public async Task<IActionResult> SaveUser([FromBody] QuickPickDefinition definition)
    {
        var saved = await this._quickPickService.SaveUserPickAsync(this.UserId, definition);
        return this.Ok(saved);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAdmin(string id)
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        var deleted = await this._quickPickService.DeleteAdminPickAsync(id);
        if (!deleted)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    [HttpDelete("user/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var deleted = await this._quickPickService.DeleteUserPickAsync(this.UserId, id);
        if (!deleted)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    [HttpPost("{id}/apply")]
    public async Task<IActionResult> Apply(string id, [FromBody] QuickPickApplyRequest request)
    {
        var state = await this._quickPickService.ApplyAsync(this.UserId, this.ProfileNo, id, request);
        return this.Ok(state);
    }

    [HttpPost("{id}/reapply")]
    public async Task<IActionResult> Reapply(string id, [FromBody] QuickPickApplyRequest request)
    {
        var state = await this._quickPickService.ReapplyAsync(this.UserId, this.ProfileNo, id, request);
        return this.Ok(state);
    }

    [HttpDelete("{id}/remove")]
    public async Task<IActionResult> Remove(string id)
    {
        var removed = await this._quickPickService.RemoveAsync(this.UserId, this.ProfileNo, id);
        if (!removed)
        {
            return this.NotFound();
        }

        return this.NoContent();
    }

    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        await this._quickPickService.SeedDefaultsAsync();
        return this.NoContent();
    }
}
