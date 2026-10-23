using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Tests.Mappings;

public class AccountLinkMappingTests
{
    private const string DiscordId = "100000000000000001";
    private const string TelegramId = "200000001";

    private static AccountLink SampleLink() => new(
        7, DiscordId, TelegramId, LinkSide.Telegram,
        new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc),
        new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void RoundTrip_KeepsIdsDefaultAndTimestamps()
    {
        var entity = SampleLink().ToEntity();
        entity.Id = 7;

        var model = entity.ToModel();

        Assert.Equal(7, model.Id);
        Assert.Equal(DiscordId, model.DiscordId);
        Assert.Equal(TelegramId, model.TelegramId);
        Assert.Equal(LinkSide.Telegram, model.DefaultDestination);
        Assert.Equal(SampleLink().DiscordVerifiedAt, model.DiscordVerifiedAt);
        Assert.Equal(SampleLink().TelegramVerifiedAt, model.TelegramVerifiedAt);
        Assert.Equal(SampleLink().CreatedAt, model.CreatedAt);
    }

    [Fact]
    public void ToEntity_LeavesIdZero()
    {
        var entity = SampleLink().ToEntity();

        Assert.Equal(0, entity.Id);
        Assert.Equal("telegram", entity.DefaultDestination);
    }

    [Fact]
    public void ToModel_ReadsDefaultDestinationCaseInsensitively()
    {
        var entity = SampleLink().ToEntity();
        entity.DefaultDestination = "Discord";

        Assert.Equal(LinkSide.Discord, entity.ToModel().DefaultDestination);
    }

    [Fact]
    public void TryParse_AcceptsEitherSideAnyCase()
    {
        Assert.True(AccountLinkKeys.TryParse("Telegram", out var s));
        Assert.Equal(LinkSide.Telegram, s);
        Assert.True(AccountLinkKeys.TryParse("discord", out var d));
        Assert.Equal(LinkSide.Discord, d);
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RefusesAnythingElse(string? value) => Assert.False(AccountLinkKeys.TryParse(value, out _));

    [Fact]
    public void Wire_AndTypeStrings_AreFixed()
    {
        Assert.Equal("discord", AccountLinkKeys.Wire(LinkSide.Discord));
        Assert.Equal("telegram", AccountLinkKeys.Wire(LinkSide.Telegram));
        Assert.Equal("discord:user", AccountLinkKeys.DiscordUserType);
        Assert.Equal("telegram:user", AccountLinkKeys.TelegramUserType);
        Assert.Equal("link_register_missing", AccountLinkKeys.RegisterMissing);
        Assert.Equal("disable_account_linking", DisableFeatureKeys.AccountLinking);
    }

    // ── LinkedPair ───────────────────────────────────────────

    private static LinkedPair Pair(bool discordExists, bool telegramExists) => new(
        TelegramId,
        SampleLink(),
        new PairSide(LinkSide.Discord, DiscordId, discordExists, discordExists ? PauseReason.None : PauseReason.NotRegistered),
        new PairSide(LinkSide.Telegram, TelegramId, telegramExists, telegramExists ? PauseReason.None : PauseReason.NotRegistered));

    [Fact]
    public void Single_IsUnlinkedAndListsOnlyTheCaller()
    {
        var pair = LinkedPair.Single(DiscordId);

        Assert.False(pair.IsLinked);
        Assert.Equal(new[] { DiscordId }, pair.ExistingHumanIds);
        Assert.Null(pair.HumanIdFor(LinkSide.Telegram));
    }

    [Fact]
    public void IsLinked_TrueWhenLinkPresent() => Assert.True(Pair(true, true).IsLinked);

    [Fact]
    public void ExistingHumanIds_ListsCallerFirst()
    {
        Assert.Equal(new[] { TelegramId, DiscordId }, Pair(true, true).ExistingHumanIds);
    }

    [Fact]
    public void ExistingHumanIds_SkipsSidesThatDoNotExist()
    {
        Assert.Equal(new[] { TelegramId }, Pair(false, true).ExistingHumanIds);
        Assert.DoesNotContain(DiscordId, Pair(false, true).ExistingHumanIds);
    }

    [Fact]
    public void HumanIdFor_ReadsTheLink()
    {
        var pair = Pair(false, true);

        Assert.Equal(DiscordId, pair.HumanIdFor(LinkSide.Discord));
        Assert.Equal(TelegramId, pair.HumanIdFor(LinkSide.Telegram));
    }

    [Fact]
    public void SideOf_FindsEachSideAndNullForStrangers()
    {
        var pair = Pair(true, true);

        Assert.Equal(LinkSide.Discord, pair.SideOf(DiscordId));
        Assert.Equal(LinkSide.Telegram, pair.SideOf(TelegramId));
        Assert.Null(pair.SideOf("999"));
        Assert.Null(LinkedPair.Single(DiscordId).SideOf("999"));
    }
}
