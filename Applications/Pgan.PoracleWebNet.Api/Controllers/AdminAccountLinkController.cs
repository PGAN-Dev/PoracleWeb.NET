using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>Admin view of every link, and the way to cut one.</summary>
[Route("api/admin/account-links")]
[RequireFeatureEnabled(DisableFeatureKeys.AccountLinking)]
public class AdminAccountLinkController(IAccountLinkRepository links, IAccountLinkService linkService) : BaseApiController
{
    private readonly IAccountLinkRepository _links = links;
    private readonly IAccountLinkService _linkService = linkService;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!this.IsAdmin || this.IsImpersonating)
        {
            return this.Forbid();
        }

        var all = await this._links.GetAllAsync();
        return this.Ok(all.Select(AccountLinkView.From));
    }

    [HttpDelete("{humanId}")]
    public async Task<IActionResult> Unlink(string humanId)
    {
        // Impersonating, UserId is somebody else; an admin's rights are the caller's own, not that account's.
        if (!this.IsAdmin || this.IsImpersonating)
        {
            return this.Forbid();
        }

        await this._linkService.UnlinkAsync(humanId);
        return this.NoContent();
    }
}
