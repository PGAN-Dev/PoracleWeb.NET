using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class LinkedAccountTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IAccountLinkRepository> _links = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly LinkedAccount _sut;

    public LinkedAccountTests()
    {
        this._gate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.AccountLinking)).ReturnsAsync(true);
        this._sut = new LinkedAccount(this._links.Object, this._humans.Object, this._gate.Object, this._cache, NullLogger<LinkedAccount>.Instance);
    }

    private static AccountLink Link() => new(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow);

    private void WithLink() => this._links.Setup(r => r.GetByHumanIdAsync(It.IsAny<string>())).ReturnsAsync(Link());

    private void WithHuman(string id, string? type, int enabled = 1, int adminDisable = 0) =>
        this._humans.Setup(h => h.GetByIdAsync(id)).ReturnsAsync(type is null ? null : new Human { Id = id, Type = type, Enabled = enabled, AdminDisable = adminDisable });

    [Fact]
    public async Task Resolve_NoLink_AnswersCallerAloneWithoutHumanReads()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord)).ReturnsAsync((AccountLink?)null);

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.IsLinked);
        Assert.Equal([Discord], pair.ExistingHumanIds);
        this._humans.Verify(h => h.GetByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Linked_BothSidesExist()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        var pair = await this._sut.ResolveAsync(Telegram);

        Assert.True(pair.IsLinked);
        Assert.True(pair.Discord!.Exists);
        Assert.True(pair.Telegram!.Exists);
        Assert.Equal(PauseReason.None, pair.Discord.Pause);
        Assert.Equal([Telegram, Discord], pair.ExistingHumanIds);
    }

    [Fact]
    public async Task Resolve_OtherSideNotRegistered_IsPausedNotRegistered()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, null);

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.Telegram!.Exists);
        Assert.Equal(PauseReason.NotRegistered, pair.Telegram.Pause);
        Assert.Equal([Discord], pair.ExistingHumanIds);
    }

    [Fact]
    public async Task Resolve_TreatsRetypedSideAsAbsent()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, "telegram:group");

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.Telegram!.Exists);
        Assert.Equal(PauseReason.NotRegistered, pair.Telegram.Pause);
    }

    [Fact]
    public async Task Resolve_SignedInSideMissing_StillAnswersOtherSide()
    {
        this.WithLink();
        this.WithHuman(Discord, null);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.Discord!.Exists);
        Assert.True(pair.Telegram!.Exists);
        Assert.Equal([Telegram], pair.ExistingHumanIds);
    }

    [Fact]
    public async Task Resolve_AdminDisabledSide_IsPaused()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType, adminDisable: 1);

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.True(pair.Telegram!.Exists);
        Assert.Equal(PauseReason.AdminDisabled, pair.Telegram.Pause);
    }

    [Fact]
    public async Task Resolve_StoppedSide_IsPausedStopped()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType, enabled: 0);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.Equal(PauseReason.Stopped, pair.Discord!.Pause);
        Assert.Equal(PauseReason.None, pair.Telegram!.Pause);
    }

    [Fact]
    public async Task Resolve_LinkingDisabled_AnswersCallerAlone()
    {
        this._gate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.AccountLinking)).ReturnsAsync(false);
        this.WithLink();

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.IsLinked);
        this._links.Verify(r => r.GetByHumanIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_CachedForOneMinute()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        await this._sut.ResolveAsync(Discord);
        await this._sut.ResolveAsync(Discord);

        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Discord), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Telegram), Times.Once);
    }

    [Fact]
    public async Task Resolve_NoLinkIsCachedToo()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord)).ReturnsAsync((AccountLink?)null);

        await this._sut.ResolveAsync(Discord);
        await this._sut.ResolveAsync(Discord);

        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Once);
    }

    [Fact]
    public async Task Resolve_HumanReadThrows_Propagates()
    {
        this.WithLink();
        this._humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.ResolveAsync(Discord));
    }

    [Fact]
    public async Task Invalidate_DropsBothIds()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);
        await this._sut.ResolveAsync(Discord);
        await this._sut.ResolveAsync(Telegram);

        this._sut.Invalidate(Discord);
        await this._sut.ResolveAsync(Discord);
        await this._sut.ResolveAsync(Telegram);

        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Exactly(2));
        this._links.Verify(r => r.GetByHumanIdAsync(Telegram), Times.Exactly(2));
        this._humans.Verify(h => h.GetByIdAsync(Discord), Times.Exactly(2));
        this._humans.Verify(h => h.GetByIdAsync(Telegram), Times.Exactly(2));
    }

    [Fact]
    public async Task Resolve_LinkReadThrows_AnswersCallerAloneAndDoesNotCacheIt()
    {
        this._links.SetupSequence(r => r.GetByHumanIdAsync(Discord))
            .ThrowsAsync(new InvalidOperationException("poracle_web is down"))
            .ReturnsAsync(Link());
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        var during = await this._sut.ResolveAsync(Discord);
        var after = await this._sut.ResolveAsync(Discord);

        Assert.False(during.IsLinked);
        Assert.Equal([Discord], during.ExistingHumanIds);
        Assert.True(after.IsLinked);
        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Exactly(2));
    }

    [Fact]
    public async Task Resolve_LinkReadSucceeds_StillCachedAfterTheFailOpenChange()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);

        var pair = await this._sut.ResolveAsync(Discord);
        await this._sut.ResolveAsync(Discord);

        Assert.True(pair.IsLinked);
        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Once);
    }

    [Fact]
    public async Task Resolve_UnreadableRow_AnswersCallerAlone()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord)).ThrowsAsync(new AccountLinkUnreadableException(1, "unknown default destination"));

        var pair = await this._sut.ResolveAsync(Discord);

        Assert.False(pair.IsLinked);
        this._humans.Verify(h => h.GetByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Cancelled_Propagates()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord)).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => this._sut.ResolveAsync(Discord));
    }

    [Fact]
    public async Task Invalidate_LinkNotCached_DropsTheGivenIdOnly()
    {
        this.WithLink();
        this.WithHuman(Discord, AccountLinkKeys.DiscordUserType);
        this.WithHuman(Telegram, AccountLinkKeys.TelegramUserType);
        await this._sut.ResolveAsync(Telegram);

        // Discord's link was never read, so Invalidate(Discord) can't learn the other id and must not throw.
        this._sut.Invalidate(Discord);
        await this._sut.ResolveAsync(Telegram);
        await this._sut.ResolveAsync(Discord);

        this._links.Verify(r => r.GetByHumanIdAsync(Telegram), Times.Once);
        this._links.Verify(r => r.GetByHumanIdAsync(Discord), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Discord), Times.Exactly(2));
        this._humans.Verify(h => h.GetByIdAsync(Telegram), Times.Once);
    }
}
