using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// The startup check on sign-in configuration. Discord is optional; the server refuses to start only
/// when Discord is half configured or when no sign-in method is configured at all.
/// </summary>
public class SignInConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static readonly (string, string)[] Discord =
        [("Discord:ClientId", "123456789012345678"), ("Discord:ClientSecret", "discord-secret")];

    private static readonly (string, string)[] Telegram =
        [("Telegram:Enabled", "true"), ("Telegram:BotToken", "123:abc"), ("Telegram:BotUsername", "pgan_bot")];

    private static readonly (string, string)[] Oidc =
    [
        ("Oidc:Enabled", "true"),
        ("Oidc:ClientId", "client"),
        ("Oidc:AuthorizationUrl", "https://idp.example.com/authorize"),
        ("Oidc:TokenUrl", "https://idp.example.com/token"),
        ("Oidc:UserInfoUrl", "https://idp.example.com/userinfo"),
    ];

    [Fact]
    public void DiscordOnlyStartsAsBefore() => Assert.Null(SignInConfiguration.Validate(Config(Discord)));

    [Fact]
    public void TelegramOnlyStarts() => Assert.Null(SignInConfiguration.Validate(Config(Telegram)));

    [Fact]
    public void OidcOnlyStarts() => Assert.Null(SignInConfiguration.Validate(Config(Oidc)));

    [Fact]
    public void AllThreeStart() => Assert.Null(SignInConfiguration.Validate(Config([.. Discord, .. Telegram, .. Oidc])));

    [Fact]
    public void DiscordIdWithoutSecretIsRefusedNamingTheMissingHalf()
    {
        var error = SignInConfiguration.Validate(Config([("Discord:ClientId", "123"), .. Telegram]));

        Assert.NotNull(error);
        Assert.Contains("half configured", error, StringComparison.Ordinal);
        Assert.Contains("DISCORD_CLIENT_SECRET) is not", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscordSecretWithoutIdIsRefusedNamingTheMissingHalf()
    {
        var error = SignInConfiguration.Validate(Config([("Discord:ClientSecret", "s"), .. Telegram]));

        Assert.NotNull(error);
        Assert.Contains("DISCORD_CLIENT_ID) is not", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceDiscordValuesCountAsAbsent() =>
        Assert.Null(SignInConfiguration.Validate(Config([("Discord:ClientId", " "), ("Discord:ClientSecret", ""), .. Telegram])));

    [Fact]
    public void NothingConfiguredIsRefused()
    {
        var error = SignInConfiguration.Validate(Config());

        Assert.NotNull(error);
        Assert.Contains("No sign-in method is configured", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TelegramSwitchedOffDoesNotCount() =>
        Assert.NotNull(SignInConfiguration.Validate(Config(("Telegram:Enabled", "false"), ("Telegram:BotToken", "123:abc"))));

    [Fact]
    public void TelegramEnabledWithoutABotTokenDoesNotCount() =>
        Assert.NotNull(SignInConfiguration.Validate(Config(("Telegram:Enabled", "true"))));

    [Fact]
    public void OidcMissingAnEndpointDoesNotCount() =>
        Assert.NotNull(SignInConfiguration.Validate(Config([.. Oidc.Where(o => o.Item1 != "Oidc:TokenUrl")])));
}
