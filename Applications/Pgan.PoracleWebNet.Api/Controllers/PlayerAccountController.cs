using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>The body of <c>POST api/accounts</c> and <c>POST api/admin/accounts</c>.</summary>
public sealed record AddPlayerAccountRequest(
    [Required, StringLength(256, MinimumLength = 1)] string PlayerId,
    [StringLength(64)] string? Label);

/// <summary>The body of the relabel actions. A null or empty label clears the label.</summary>
public sealed record RelabelPlayerAccountRequest([StringLength(64)] string? Label);

/// <summary>The body of the restore actions: the token an unlink answered with.</summary>
public sealed record RestorePlayerAccountRequest([Required, StringLength(4096, MinimumLength = 1)] string UndoToken);

/// <summary>
/// Linked game accounts, over PoracleNG's <c>/api/v2/humans/{id}/accounts</c>. Personal shiny alerts match
/// nothing for a human with no linked account.
/// </summary>
/// <remarks>
/// <para>
/// <b>Player ids never leave the server.</b> A player id is the input to someone's shiny rolls, and the maps
/// that link accounts (ReactMap's personal shiny support, Diadem) push it into PoracleNG through its API.
/// This controller answers every account as a <see cref="PlayerAccountView"/> whose id is an opaque handle
/// (<see cref="PlayerAccountHandles"/>), resolves a handle back to its account by recomputing it over the
/// human's list, and answers an unlink with a short-lived sealed undo token rather than the id it removed.
/// The one way an id arrives is the administrator's add, from Admin > Users.
/// </para>
/// <para>
/// PoracleNG's account API trusts its caller and ignores its own <c>[player_accounts] self_service</c>
/// setting, so this controller is where it is enforced: a trainer may write their own accounts only when
/// self-service is on, and an administrator may always manage any human's.
/// </para>
/// <para>
/// A trainer write is also refused while impersonating, because <c>UserId</c> is then the inspected account
/// and an unguarded write would change somebody else's link (the #663 shape). Reads stay open.
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
    IPersonalShinyConfigService personalConfig,
    PlayerAccountHandles handles,
    IDataProtectionProvider dataProtection) : BaseApiController
{
    /// <summary>Long enough to notice a slip and press Undo; short enough that a token is not a credential.</summary>
    internal static readonly TimeSpan UndoLifetime = TimeSpan.FromMinutes(5);

    private readonly IPoracleAccountProxy _proxy = proxy;
    private readonly IPoracleV2SchemaService _v2Schema = v2Schema;
    private readonly IPersonalShinyConfigService _personalConfig = personalConfig;
    private readonly PlayerAccountHandles _handles = handles;
    private readonly ITimeLimitedDataProtector _undo =
        dataProtection.CreateProtector("Pgan.PoracleWebNet.PlayerAccountUndo.v1").ToTimeLimitedDataProtector();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var unsupported = await this.RefuseUnsupportedAsync(cancellationToken);
        if (unsupported is not null)
        {
            return unsupported;
        }

        return await this.RunAsync(async () => this.Ok(await this.ViewsAsync(this.UserId, cancellationToken)));
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
            this.Ok(this.View(this.UserId, await this._proxy.AddAsync(this.UserId, request.PlayerId, request.Label, cancellationToken))));
    }

    [HttpPut("{accountId}")]
    public async Task<IActionResult> Relabel(string accountId, [FromBody] RelabelPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        return refusal ?? await this.RelabelCoreAsync(this.UserId, accountId, request, cancellationToken);
    }

    [HttpDelete("{accountId}")]
    public async Task<IActionResult> Delete(string accountId, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        return refusal ?? await this.DeleteCoreAsync(this.UserId, accountId, cancellationToken);
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] RestorePlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseTrainerWriteAsync(cancellationToken);
        return refusal ?? await this.RestoreCoreAsync(this.UserId, request, cancellationToken);
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

        return await this.RunAsync(async () => this.Ok(await this.ViewsAsync(id, cancellationToken)));
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
            this.Ok(this.View(id, await this._proxy.AddAsync(id, request.PlayerId, request.Label, cancellationToken))));
    }

    [HttpPut("~/api/admin/accounts/{accountId}")]
    public async Task<IActionResult> AdminRelabel(
        [FromQuery] string id, string accountId, [FromBody] RelabelPlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        return refusal ?? await this.RelabelCoreAsync(id, accountId, request, cancellationToken);
    }

    [HttpDelete("~/api/admin/accounts/{accountId}")]
    public async Task<IActionResult> AdminDelete([FromQuery] string id, string accountId, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        return refusal ?? await this.DeleteCoreAsync(id, accountId, cancellationToken);
    }

    [HttpPost("~/api/admin/accounts/restore")]
    public async Task<IActionResult> AdminRestore([FromQuery] string id, [FromBody] RestorePlayerAccountRequest request, CancellationToken cancellationToken)
    {
        var refusal = await this.RefuseAdminAsync(cancellationToken);
        return refusal ?? await this.RestoreCoreAsync(id, request, cancellationToken);
    }

    private Task<IActionResult> RelabelCoreAsync(
        string humanId, string accountId, RelabelPlayerAccountRequest request, CancellationToken cancellationToken) =>
        this.RunAsync(async () =>
        {
            var account = await this.ResolveAsync(humanId, accountId, cancellationToken);
            if (account is null)
            {
                return this.NoSuchAccount();
            }

            await this._proxy.RelabelAsync(humanId, account.PlayerId, request.Label ?? string.Empty, cancellationToken);
            return this.NoContent();
        });

    private Task<IActionResult> DeleteCoreAsync(string humanId, string accountId, CancellationToken cancellationToken) =>
        this.RunAsync(async () =>
        {
            var account = await this.ResolveAsync(humanId, accountId, cancellationToken);
            if (account is null)
            {
                return this.NoSuchAccount();
            }

            await this._proxy.DeleteAsync(humanId, account.PlayerId, cancellationToken);
            var sealedAccount = JsonSerializer.Serialize(new UndoPayload(humanId, account.PlayerId, account.Label));
            return this.Ok(new { undoToken = this._undo.Protect(sealedAccount, UndoLifetime) });
        });

    private Task<IActionResult> RestoreCoreAsync(string humanId, RestorePlayerAccountRequest request, CancellationToken cancellationToken) =>
        this.RunAsync(async () =>
        {
            UndoPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<UndoPayload>(this._undo.Unprotect(request.UndoToken));
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                payload = null;
            }

            // A token is only good for the human it was issued for: an admin's undo cannot re-link a trainer's
            // account onto somebody else, and a stale or tampered token says so instead of guessing.
            if (payload is null || payload.HumanId != humanId)
            {
                return this.BadRequest(new
                {
                    error = "That undo has expired.",
                    code = "undo_expired"
                });
            }

            return this.Ok(this.View(humanId, await this._proxy.AddAsync(humanId, payload.PlayerId, payload.Label, cancellationToken)));
        });

    private async Task<IReadOnlyList<PlayerAccountView>> ViewsAsync(string humanId, CancellationToken cancellationToken) =>
        [.. (await this._proxy.ListAsync(humanId, cancellationToken)).Select(a => this.View(humanId, a))];

    private PlayerAccountView View(string humanId, PlayerAccount account) =>
        new(this._handles.For(humanId, account.PlayerId), account.Label, account.CreatedAt);

    private async Task<PlayerAccount?> ResolveAsync(string humanId, string accountId, CancellationToken cancellationToken) =>
        (await this._proxy.ListAsync(humanId, cancellationToken))
            .FirstOrDefault(a => string.Equals(this._handles.For(humanId, a.PlayerId), accountId, StringComparison.Ordinal));

    private ObjectResult NoSuchAccount() =>
        this.StatusCode(StatusCodes.Status404NotFound, new
        {
            error = "That game account is not linked."
        });

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

        // An administrator managing their own accounts is not subject to the self-service setting.
        if (this.IsAdmin)
        {
            return null;
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

    private sealed record UndoPayload(string HumanId, string PlayerId, string? Label);
}
