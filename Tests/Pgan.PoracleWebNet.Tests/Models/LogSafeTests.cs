using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// A webhook's human id is its URL, token included, so any log line naming one leaked a credential that
/// lets anyone post to the channel. <see cref="LogSafe.Id"/> keeps enough to tell rows apart. See #1038.
/// </summary>
public class LogSafeTests
{
    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456/abcDEF-token_value", "https://discord.com/api/webhooks/123456/***")]
    [InlineData("https://discord.com/api/webhooks/123456/abcDEF-token_value/slack", "https://discord.com/api/webhooks/123456/***")]
    [InlineData("https://discord.com/api/webhooks/123456/token?wait=true&thread_id=9", "https://discord.com/api/webhooks/123456/***")]
    [InlineData("https://discordapp.com/api/webhooks/123456/token", "https://discordapp.com/api/webhooks/123456/***")]
    [InlineData("https://canary.discord.com/api/v10/webhooks/123456/token", "https://canary.discord.com/api/v10/webhooks/123456/***")]
    [InlineData("HTTPS://Discord.com/API/Webhooks/123456/token", "https://discord.com/API/Webhooks/123456/***")]
    public void DiscordWebhookTokenIsMasked(string id, string expected)
    {
        Assert.Equal(expected, LogSafe.Id(id));
    }

    [Theory]
    [InlineData("http://hooks.example.net:8080/services/T000/B000/XXXXSECRET", "http://hooks.example.net:8080/***")]
    [InlineData("https://hooks.slack.com/services/T000/B000/XXXXSECRET", "https://hooks.slack.com/***")]
    [InlineData("https://example.net/secret-in-the-only-segment", "https://example.net/***")]
    [InlineData("https://example.net/hook?token=secret", "https://example.net/***")]
    [InlineData("https://user:pass@example.net/hook/secret", "https://example.net/***")]
    [InlineData("https://example.net", "https://example.net/***")]
    [InlineData("https://api.telegram.org/bot123456:ABC-secret/sendMessage", "https://api.telegram.org/***")]
    [InlineData("https://discord.com/api/webhooks/not-a-snowflake/token", "https://discord.com/***")]
    [InlineData("https://discord.com/api/channels/123456/messages", "https://discord.com/***")]
    public void OtherUrlsKeepSchemeAndHostOnly(string id, string expected)
    {
        Assert.Equal(expected, LogSafe.Id(id));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456/supersecret")]
    [InlineData("https://hooks.example.net/services/T000/B000/supersecret")]
    [InlineData("https://example.net/a?supersecret")]
    [InlineData("https://example.net/a#supersecret")]
    [InlineData("https://example.net/supersecret/hook")]
    [InlineData("https://api.telegram.org/bot123456:supersecret/sendMessage")]
    [InlineData("https://discord.com/api/webhooks/supersecret/other")]
    [InlineData("https://discord.com/api/webhooks/12ab34/supersecret")]
    public void TokenNeverSurvives(string id)
    {
        Assert.DoesNotContain("supersecret", LogSafe.Id(id), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  https://discord.com/api/webhooks/123456/supersecret  ", "https://discord.com/api/webhooks/123456/***")]
    [InlineData("	http://example.net/hook/supersecret", "http://example.net/***")]
    [InlineData("discord.com/api/webhooks/123456/supersecret", "https://discord.com/api/webhooks/123456/***")]
    [InlineData("discordapp.com/api/webhooks/123456/supersecret", "https://discordapp.com/api/webhooks/123456/***")]
    public void PaddedOrSchemelessWebhookUrlsAreStillMasked(string id, string expected)
    {
        Assert.Equal(expected, LogSafe.Id(id));
    }

    [Theory]
    [InlineData("123456789012345678")]
    [InlineData("-1001234567890")]
    [InlineData("-1001234567890:42")]
    [InlineData("some-name")]
    [InlineData(" padded name ")]
    [InlineData("raid alerts / webhooks")]
    [InlineData("")]
    public void NonUrlIdsAreUnchanged(string id)
    {
        Assert.Equal(id, LogSafe.Id(id));
    }

    [Fact]
    public void NullIsNamed()
    {
        Assert.Equal("(none)", LogSafe.Id(null));
    }
}
