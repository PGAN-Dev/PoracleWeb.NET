using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/quick-picks")]
public class QuickPickController(
    IQuickPickService quickPickService,
    ISiteSettingService siteSettingService) : BaseApiController
{
    private readonly IQuickPickService _quickPickService = quickPickService;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var picks = await this._quickPickService.GetAllAsync(this.UserId, this.ProfileNo);
        return this.Ok(picks);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        // Scoped: a global pick is public, a user pick is only its owner's. Returning any row by id leaked
        // another user's private pick along with their Discord ID in ownerUserId.
        var pick = this.IsAdmin
            ? await this._quickPickService.GetByIdAsync(id)
            : await this._quickPickService.GetVisibleByIdAsync(this.UserId, id);

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
        try
        {
            var saved = await this._quickPickService.SaveUserPickAsync(this.UserId, definition);
            return this.Ok(saved);
        }
        catch (UnauthorizedAccessException)
        {
            // The id belongs to a global pick or another user's pick.
            return this.Forbid();
        }
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
        var invalid = this.ValidateApplyRequest(request);
        if (invalid is not null)
        {
            return invalid;
        }

        try
        {
            var state = await this._quickPickService.ApplyAsync(this.UserId, this.ProfileNo, id, request);
            return this.Ok(state);
        }
        catch (InvalidOperationException ex)
        {
            // Unknown id, or a definition carrying an alarm type the applier cannot handle. The sibling
            // GET and DELETE already 404 on an unknown id; this used to be an unhandled 500.
            return this.NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/reapply")]
    public async Task<IActionResult> Reapply(string id, [FromBody] QuickPickApplyRequest request)
    {
        var invalid = this.ValidateApplyRequest(request);
        if (invalid is not null)
        {
            return invalid;
        }

        try
        {
            var state = await this._quickPickService.ReapplyAsync(this.UserId, this.ProfileNo, id, request);
            return this.Ok(state);
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rejects override values the alarm endpoints would refuse anyway. Previously the throw happened
    /// after the alarms were created but before the applied-state write, leaving the pick un-applied
    /// with no cleanup path.
    /// </summary>
    private BadRequestObjectResult? ValidateApplyRequest(QuickPickApplyRequest? request)
    {
        if (request?.Clean is { } clean && clean is < 0 or > 7)
        {
            return this.BadRequest(new
            {
                error = "clean must be between 0 and 7 (a 3-bit mask: 1 auto-delete, 2 edit, 4 summary)."
            });
        }

        if (request?.Distance is { } distance && distance < 0)
        {
            return this.BadRequest(new { error = "distance cannot be negative." });
        }

        return null;
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

    /// <summary>Marks that the built-in presets have been created once. See #634, #662.</summary>
    private const string QuickPicksSeededKey = "quick_picks_seeded";

    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (!this.IsAdmin)
        {
            return this.Forbid();
        }

        await this._quickPickService.SeedDefaultsAsync();

        // Whether an installation has been seeded is a property of the installation. The SPA used to
        // record it in localStorage, so a second admin or a different browser reseeded anyway, and a
        // failed seed latched the flag and never retried. Written here, after the seed actually
        // succeeded. See #662.
        await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
        {
            Key = QuickPicksSeededKey,
            Value = "true",
            Category = "admin",
            ValueType = "boolean",
        });
        return this.NoContent();
    }
}
