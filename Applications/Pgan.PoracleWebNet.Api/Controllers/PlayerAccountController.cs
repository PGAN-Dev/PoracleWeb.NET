using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>The body of <c>POST api/accounts</c> and <c>POST api/admin/accounts</c>.</summary>
public sealed record AddPlayerAccountRequest(
    [property: Required, StringLength(256, MinimumLength = 1)] string PlayerId,
    [property: StringLength(64)] string? Label);

/// <summary>The body of the relabel actions. A null or empty label clears the label.</summary>
public sealed record RelabelPlayerAccountRequest([property: StringLength(64)] string? Label);

/// <summary>
/// Linked game accounts (player ids), over PoracleNG's <c>/api/v2/humans/{id}/accounts</c>. Personal shiny
/// alerts match nothing for a human with no linked account.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG's account API trusts its caller and ignores its own <c>[player_accounts] self_service</c>
/// setting, so this controller is where it is enforced: a trainer may write their own accounts only when
/// self-service is on, and an administrator may always manage any human's.
/// </para>
/// <para>
/// A trainer write is also refused while impersonating, because <c>UserId</c> is then the inspected
/// account and an unguarded write would change somebody else's link (the #663 shape). Reads stay open.
/// </para>
/// <para>
/// Player ids are case-sensitive and reach the proxy exactly as received.
/// </para>
/// </remarks>
[Route("api/accounts")]
[EnableRateLimiting("accounts")]
public class PlayerAccountController(
    IPoracleAccountProxy proxy,
    IPoracleV2SchemaService v2Schema,
    IPersonalShinyConfigService personalConfig) : BaseApiController
{
    private readonly IPoracleAccountProxy _proxy = proxy;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly IPersonalShinyConfigService _personalConfig = personalConfig;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var unsupported = await this.RefuseUnsupportedAsync(cancellationToken);
        if (unsupported is not null)
        {
            return unsupported;
        }

        return await this.RunAsync(async () => this.Ok(await this._proxy.ListAsync(this.UserId, cancellationToken)));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
            this.Ok(await this._proxy.AddAsync(this.UserId, request.PlayerId, request.Label, cancellationToken)));
    }

    [HttpPut("{playerId}")]
    public async Task<IActionResult> Relabel(string playerId, [FromBody] RelabelPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
        {
            await this._proxy.RelabelAsync(this.UserId, playerId, request.Label ?? string.Empty, cancellationToken);
            return this.NoContent();
        });
    }

    [HttpDelete("{playerId}")]
    public async Task<IActionResult> Delete(string playerId, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
        {
            await this._proxy.DeleteAsync(this.UserId, playerId, cancellationToken);
            return this.NoContent();
        });
    }

    // Administrators manage any human's accounts whatever self-service says.

    [HttpGet("~/api/admin/accounts")]
    public async Task<IActionResult> AdminList([FromQuery] string id, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () => this.Ok(await this._proxy.ListAsync(id, cancellationToken)));
    }

    [HttpPost("~/api/admin/accounts")]
    public async Task<IActionResult> AdminAdd([FromQuery] string id, [FromBody] AddPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
            this.Ok(await this._proxy.AddAsync(id, request.PlayerId, request.Label, cancellationToken)));
    }

    [HttpPut("~/api/admin/accounts/{playerId}")]
    public async Task<IActionResult> AdminRelabel(
        [FromQuery] string id, string playerId, [FromBody] RelabelPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
        {
            await this._proxy.RelabelAsync(id, playerId, request.Label ?? string.Empty, cancellationToken);
            return this.NoContent();
        });
    }

    [HttpDelete("~/api/admin/accounts/{playerId}")]
    public async Task<IActionResult> AdminDelete([FromQuery] string id, string playerId, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return await this.RunAsync(async () =>
        {
            await this._proxy.DeleteAsync(id, playerId, cancellationToken);
            return this.NoContent();
        });
    }

    /// <summary>Reads included, so the SPA can tell an old server from an empty list.</summary>
    private async Task<IActionResult?> RefuseUnsupportedAsync(CancellationToken cancellationToken)
    {
        var capabilities = await this._v2Schema.GetAsync(cancellationToken);
        return capabilities.PlayerAccounts
            ? null
            : this.StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = "This Poracle server does not support game accounts."
            });
    }

    private async Task<IActionResult?> RefuseTrainerWriteAsync(CancellationToken cancellationToken)
    {
        var unsupported = await this.RefuseUnsupportedAsync(cancellationToken);
        if (unsupported is not null)
        {
            return unsupported;
        }

        if (this.IsImpersonating)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Game accounts cannot be changed while viewing another account."
            });
        }

        var config = await this._personalConfig.GetAsync();
        if (!config.SelfService)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Only an administrator can link game accounts on this server.",
                code = "self_service_disabled"
            });
        }

        return null;
    }

    private async Task<IActionResult?> RefuseAdminAsync(CancellationToken cancellationToken)
    {
        var unsupported = await this.RefuseUnsupportedAsync(cancellationToken);
        if (unsupported is not null)
        {
            return unsupported;
        }

        return this.IsAdmin ? null : this.Forbid();
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PoracleRequestRefusedException ex)
        {
            return this.StatusCode(ex.StatusCode, new
            {
                error = ex.Message
            });
        }
    }
}
