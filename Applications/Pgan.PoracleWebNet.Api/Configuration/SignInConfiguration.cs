namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Which sign-in methods the server configuration provides, and the startup check built on it.
/// Discord is optional: a site may sign people in through Telegram or an OIDC provider alone. The
/// check refuses only what can't work: Discord half configured, or no method at all, which would
/// leave a site nobody can sign in to.
/// </summary>
public static class SignInConfiguration
{
    /// <summary>Discord sign-in needs both the OAuth2 client id and its secret.</summary>
    public static bool DiscordConfigured(DiscordSettings discord) =>
        !string.IsNullOrWhiteSpace(discord.ClientId) && !string.IsNullOrWhiteSpace(discord.ClientSecret);

    /// <summary>
    /// Telegram sign-in is on in server config and has the bot token its login data is verified
    /// against. The bot username can still come from the <c>telegram_bot</c> site setting.
    /// </summary>
    public static bool TelegramConfigured(TelegramSettings telegram) =>
        telegram.Enabled && !string.IsNullOrWhiteSpace(telegram.BotToken);

    /// <summary>The same test <c>AuthController</c> applies before offering the OIDC provider.</summary>
    public static bool OidcConfigured(OidcSettings oidc) =>
        oidc.Enabled
        && !string.IsNullOrWhiteSpace(oidc.AuthorizationUrl)
        && !string.IsNullOrWhiteSpace(oidc.TokenUrl)
        && !string.IsNullOrWhiteSpace(oidc.UserInfoUrl)
        && !string.IsNullOrWhiteSpace(oidc.ClientId);

    /// <summary>
    /// Returns why the configuration can't start, or null when it can. Read after Program.cs has
    /// inferred <c>Telegram:Enabled</c> and <c>Oidc:Enabled</c> from their credentials.
    /// </summary>
    public static string? Validate(IConfiguration configuration)
    {
        var discord = configuration.GetSection("Discord").Get<DiscordSettings>() ?? new DiscordSettings();
        var telegram = configuration.GetSection("Telegram").Get<TelegramSettings>() ?? new TelegramSettings();
        var oidc = configuration.GetSection("Oidc").Get<OidcSettings>() ?? new OidcSettings();

        var hasId = !string.IsNullOrWhiteSpace(discord.ClientId);
        var hasSecret = !string.IsNullOrWhiteSpace(discord.ClientSecret);
        if (hasId != hasSecret)
        {
            var (set, missing) = hasId
                ? ("Discord:ClientId (DISCORD_CLIENT_ID)", "Discord:ClientSecret (DISCORD_CLIENT_SECRET)")
                : ("Discord:ClientSecret (DISCORD_CLIENT_SECRET)", "Discord:ClientId (DISCORD_CLIENT_ID)");
            return $"Discord sign-in is half configured: {set} is set but {missing} is not. "
                + "Set both to offer Discord sign-in, or remove both to run without it.";
        }

        if (!DiscordConfigured(discord) && !TelegramConfigured(telegram) && !OidcConfigured(oidc))
        {
            return "No sign-in method is configured, so nobody could sign in. Configure at least one: "
                + "Discord (DISCORD_CLIENT_ID and DISCORD_CLIENT_SECRET), "
                + "Telegram (TELEGRAM_BOT_TOKEN and TELEGRAM_BOT_USERNAME), "
                + "or an OIDC provider (OIDC_CLIENT_ID, OIDC_AUTHORIZATION_URL, OIDC_TOKEN_URL and OIDC_USERINFO_URL).";
        }

        return null;
    }
}
