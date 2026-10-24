using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
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

/// <summary>Body of <c>POST api/account-link/resolve</c>: which setting, which side to keep, and the fingerprint the banner showed.</summary>
public sealed record ResolveDriftRequest(string? Field, string? Keep, string? Expected);

/// <summary>
/// Pairs the signed-in Discord or Telegram account with the other platform's. The Telegram half is one
/// request carrying the login-widget fields; the Discord half is OAuth, started here and finished in
/// <c>AuthController.DiscordCallback</c>.
/// </summary>
[Route("api/account-link")]
[RequireFeatureEnabled(DisableFeatureKeys.AccountLinking)]
[EnableRateLimiting("auth-read")]
public partial class AccountLinkController(
    IAccountLinkService links,
    ILinkedAccount linkedAccount,
    ISiteSettingService siteSettings,
    IDataProtectionProvider dataProtection,
    IOptions<DiscordSettings> discordSettings,
    IOptions<TelegramSettings> telegramSettings,
    IConfiguration configuration,
    IPairDriftService drift,
    ILogger<AccountLinkController> logger) : BaseApiController
{
    private readonly string[] _allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    private readonly IDataProtectionProvider _dataProtection = dataProtection;
    private readonly DiscordSettings _discordSettings = discordSettings.Value;
    private readonly IPairDriftService _drift = drift;
    private readonly ILogger<AccountLinkController> _logger = logger;
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

        if (string.IsNullOrEmpty(this._discordSettings.ClientId) || await this._siteSettings.IsSwitchedOffAsync("enable_discord"))
        {
            return Refused(LinkRefusalCode.NotPermitted, "Discord sign-in is not available on this site.");
        }

        var selfOrigin = this._publicOrigin ?? $"{this.Request.Scheme}://{this.Request.Host}";
        var url = DiscordAuthorizeRequest.Build(this.HttpContext, this._discordSettings, selfOrigin, this._allowedOrigins, out _, out var state);

        // Who is linking, sealed, so the callback can act for them without a session token.
        this.Response.Cookies.Append(
            AccountLinkTicket.CookieName,
            AccountLinkTicket.Seal(this._dataProtection, this.UserId, state, DateTimeOffset.UtcNow),
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

        if (!this._telegramSettings.Enabled || await this._siteSettings.IsSwitchedOffAsync("enable_telegram"))
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

        // A drift read that fails must not take the rest of the status with it: the banner is the optional part.
        object? drift;
        try
        {
            drift = (await this._drift.GetAsync(pair))
                .Select(d => new { field = DriftFields.Wire(d.Field), discord = d.Discord, telegram = d.Telegram, fingerprint = d.Fingerprint })
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDriftReadFailed(this._logger, ex);
            drift = null;
        }

        // Optional like the banner: a failed read answers null rather than taking the status down.
        string? legacyProfileZero = null;
        try
        {
            var legacy = await this._drift.GetLegacyProfileZeroAsync(pair);
            legacyProfileZero = legacy is { } side ? AccountLinkKeys.Wire(side) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDriftReadFailed(this._logger, ex);
        }

        return this.Ok(new
        {
            linked = true,
            link = AccountLinkView.From(pair.Link!),
            sides,
            defaultDestination = AccountLinkKeys.Wire(pair.Link!.DefaultDestination),
            drift,
            legacyProfileZero,
        });
    }

    /// <summary>Makes the other side match the kept one, for one setting the person chose.</summary>
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveDriftRequest request)
    {
        if (this.IsImpersonating)
        {
            return this.Forbid();
        }

        if (!DriftFields.TryParse(request?.Field, out var field) || !AccountLinkKeys.TryParse(request?.Keep, out var keep)
            || string.IsNullOrEmpty(request?.Expected))
        {
            return this.BadRequest(new { error = "field, keep and expected are required." });
        }

        var pair = await this._linkedAccount.ResolveAsync(this.UserId);
        if (!pair.IsLinked)
        {
            return this.BadRequest(new { error = "There is no linked account." });
        }

        await this._drift.ResolveAsync(pair, field, keep, request.Expected);
        return this.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read drift between linked accounts; status is answered without it")]
    private static partial void LogDriftReadFailed(ILogger logger, Exception ex);

    private static string PauseWire(PauseReason reason) => reason switch
    {
        PauseReason.None => "none",
        PauseReason.NotRegistered => "not_registered",
        PauseReason.AdminDisabled => "admin_disabled",
        PauseReason.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static ObjectResult Refused(LinkRefusalCode code, string message) =>
        new(new { error = message, code = LinkRefusalException.Wire(code) })
        {
            StatusCode = LinkRefusalExceptionFilter.StatusFor(code),
        };
}
