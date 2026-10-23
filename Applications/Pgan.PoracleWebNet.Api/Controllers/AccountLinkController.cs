using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>Body of <c>PUT api/account-link/default</c>.</summary>
public sealed record DefaultDestinationRequest(string? Destination);

/// <summary>
/// Pairs the signed-in Discord or Telegram account with the other platform's. The Telegram half is one
/// request carrying the login-widget fields; the Discord half is OAuth, started here and finished in
/// <c>AuthController.DiscordCallback</c>.
/// </summary>
[Route("api/account-link")]
[RequireFeatureEnabled(DisableFeatureKeys.AccountLinking)]
[EnableRateLimiting("auth-read")]
public class AccountLinkController(
    IAccountLinkService links,
    ILinkedAccount linkedAccount,
    ISiteSettingService siteSettings,
    IDataProtectionProvider dataProtection,
    IOptions<DiscordSettings> discordSettings,
    IOptions<TelegramSettings> telegramSettings,
    IConfiguration configuration) : BaseApiController
{
    private readonly string[] _allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    private readonly IDataProtectionProvider _dataProtection = dataProtection;
    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly ILinkedAccount _linkedAccount = linkedAccount;
    private readonly IAccountLinkService _links = links;
    private readonly string? _publicOrigin = PublicOrigin.NormalizeOrNull(configuration["PublicUrl"]);
    private readonly ISiteSettingService _siteSettings = siteSettings;
    private readonly TelegramSettings _telegramSettings = telegramSettings.Value;

    [HttpPost("discord/start")]
    public async Task<IActionResult> DiscordStart()
    {
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        if (string.IsNullOrEmpty(this._discordSettings.ClientId) || await IsOff(this._siteSettings, "enable_discord"))
        {
            return Refused(LinkRefusalCode.NotPermitted, "Discord sign-in is not available on this site.");
        }

        var selfOrigin = this._publicOrigin ?? $"{this.Request.Scheme}://{this.Request.Host}";
        var url = DiscordAuthorizeRequest.Build(this.HttpContext, this._discordSettings, selfOrigin, this._allowedOrigins, out _);

        // Who is linking, sealed, so the callback can act for them without a session token.
        this.Response.Cookies.Append(
            AccountLinkTicket.CookieName,
            AccountLinkTicket.Seal(this._dataProtection, this.UserId, DateTimeOffset.UtcNow),
            DiscordAuthorizeRequest.CookieOptions(this.Request));

        return this.Ok(new { url });
    }

    [HttpPost("telegram")]
    public async Task<IActionResult> TelegramLink([FromBody] Dictionary<string, string> widget)
    {
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        if (!this._telegramSettings.Enabled || await IsOff(this._siteSettings, "enable_telegram"))
        {
            return Refused(LinkRefusalCode.NotPermitted, "Telegram sign-in is not available on this site.");
        }

        var verified = TelegramWidgetVerifier.Verify(
            widget ?? [], this._telegramSettings.BotToken, TelegramWidgetVerifier.LinkMaxAge, DateTimeOffset.UtcNow);
        if (!verified.Valid)
        {
            return verified.Failure == "expired"
                ? Refused(LinkRefusalCode.Expired, "That Telegram login has expired. Try again.")
                : Refused(LinkRefusalCode.BadId, "That Telegram login could not be verified.");
        }

        var link = await this._links.LinkAsync(
            this.UserId, LinkSide.Telegram, verified.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), verified.Name);
        return this.Ok(new { linked = AccountLinkView.From(link) });
    }

    [HttpDelete]
    public async Task<IActionResult> Unlink()
    {
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        await this._links.UnlinkAsync(this.UserId);
        return this.NoContent();
    }

    [HttpPut("default")]
    public async Task<IActionResult> SetDefault([FromBody] DefaultDestinationRequest request)
    {
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        if (!AccountLinkKeys.TryParse(request?.Destination, out var side))
        {
            return this.BadRequest(new { error = "destination must be discord or telegram." });
        }

        await this._links.SetDefaultAsync(this.UserId, side);
        return this.NoContent();
    }

    /// <summary>The caller's link and each side's state. Unlinked, this reads nothing from PoracleNG.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var pair = await this._linkedAccount.ResolveAsync(this.UserId);
        if (!pair.IsLinked)
        {
            return this.Ok(new { linked = false });
        }

        var sides = new[] { pair.Discord, pair.Telegram }
            .Where(s => s is not null)
            .Select(s => new
            {
                side = AccountLinkKeys.Wire(s!.Side),
                exists = s.Exists,
                pause = PauseWire(s.Pause),
            })
            .ToArray();

        return this.Ok(new
        {
            linked = true,
            link = AccountLinkView.From(pair.Link!),
            sides,
            defaultDestination = AccountLinkKeys.Wire(pair.Link!.DefaultDestination),
        });
    }

    private static string PauseWire(PauseReason reason) => reason switch
    {
        PauseReason.None => "none",
        PauseReason.NotRegistered => "not_registered",
        PauseReason.AdminDisabled => "admin_disabled",
        PauseReason.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static async Task<bool> IsOff(ISiteSettingService settings, string key) =>
        string.Equals(await settings.GetValueAsync(key), "false", StringComparison.OrdinalIgnoreCase);

    private static ObjectResult Refused(LinkRefusalCode code, string message) =>
        new(new { error = message, code = LinkRefusalException.Wire(code) })
        {
            StatusCode = LinkRefusalExceptionFilter.StatusFor(code),
        };
}
