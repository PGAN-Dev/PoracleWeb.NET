using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class HumanKindTests
{
    [Theory]
    [InlineData("discord:user", true)]
    [InlineData("telegram:user", true)]
    [InlineData("oidc:user", true)]
    [InlineData("DISCORD:USER", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("webhook", false)]
    [InlineData("Webhook", false)]
    [InlineData("discord:channel", false)]
    [InlineData("discord:thread", false)]
    [InlineData("telegram:channel", false)]
    [InlineData("telegram:group", false)]
    [InlineData("telegram:topic", false)]
    [InlineData("x:unknown", false)]
    public void IsPersonFollowsUpstreamsUserSuffix(string? type, bool expected) => Assert.Equal(expected, HumanKind.IsPerson(type));

    [Theory]
    [InlineData("x:unknown")]
    [InlineData("discord:user")]
    [InlineData(null)]
    public void OnlyTheSixKnownTypesGrantPower(string? type) => Assert.False(HumanKind.IsKnownDestination(type));

    [Fact]
    public void AnUnknownTypeIsListedAsADestination() => Assert.Equal("destination", HumanKind.KindOf("x:unknown"));

    [Fact]
    public void MentionIsDiscordAndWebhookOnly()
    {
        Assert.True(HumanKind.MentionCapable.Contains("discord:thread"));
        Assert.False(HumanKind.MentionCapable.Contains("telegram:group"));
    }

    [Theory]
    [InlineData("discord:channel", "123456789012345678")]
    [InlineData("discord:thread", "123456789012345678")]
    [InlineData("telegram:channel", "-1001234567890")]
    [InlineData("telegram:group", "-1234567890")]
    [InlineData("telegram:topic", "-1001234567890:42")]
    [InlineData("webhook", "https://discord.com/api/webhooks/1/abc")]
    [InlineData("webhook", "http://host:8080/path")]
    public void AcceptsEachKindsRealId(string type, string id) => Assert.Null(HumanKind.ValidateNewId(type, id));

    [Theory]
    [InlineData("discord:channel", "12a")]
    [InlineData("discord:channel", "123456789012345678901")]
    [InlineData("telegram:group", "1234")]
    [InlineData("telegram:topic", "-100:0")]
    [InlineData("telegram:topic", "-100")]
    [InlineData("telegram:topic", "100:5")]
    [InlineData("discord:channel", "123\u0007")]
    [InlineData("webhook", "discord.com/api/webhooks/1/abc")]
    [InlineData("webhook", "https://host/a\u0007")]
    [InlineData("webhook", " ")]
    [InlineData("x:unknown", "123")]
    public void RefusesTheImpossible(string type, string id) => Assert.NotNull(HumanKind.ValidateNewId(type, id));
}
