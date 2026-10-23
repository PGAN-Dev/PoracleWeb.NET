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
}
