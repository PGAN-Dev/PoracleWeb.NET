using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class AccountLinkServiceTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IAccountLinkRepository> _links = new();
    private readonly Mock<IHumanService> _humans = new();
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Mock<ILinkedHumanProvisioner> _provisioner = new();
    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly AccountLinkService _sut;

    public AccountLinkServiceTests()
    {
        this._links.Setup(r => r.AddAsync(It.IsAny<AccountLink>()))
            .ReturnsAsync((AccountLink l) => l with { Id = 9 });
        this._sut = new AccountLinkService(
            this._links.Object, this._humans.Object, this._settings.Object, this._provisioner.Object,
            this._linked.Object, NullLogger<AccountLinkService>.Instance);
    }

    private void Human(string id, string type) =>
        this._humans.Setup(h => h.GetByIdAsync(id)).ReturnsAsync(new Human { Id = id, Type = type, Name = "Riverside" });

    private void Missing(string id) => this._humans.Setup(h => h.GetByIdAsync(id)).ReturnsAsync((Human?)null);

    private static async Task<LinkRefusalException> Refused(Task task) => await Assert.ThrowsAsync<LinkRefusalException>(() => task);

    [Fact]
    public async Task Link_DiscordCallerAddsTelegram_StoresDefaultDiscord()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);

        var link = await this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town");

        Assert.Equal(Discord, link.DiscordId);
        Assert.Equal(Telegram, link.TelegramId);
        Assert.Equal(LinkSide.Discord, link.DefaultDestination);
        this._provisioner.Verify(p => p.CreateMirrorAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        this._linked.Verify(l => l.Invalidate(Discord), Times.Once);
        this._linked.Verify(l => l.Invalidate(Telegram), Times.Once);
    }

    [Fact]
    public async Task Link_TelegramCallerAddsDiscord_StoresDefaultTelegram()
    {
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);
        this.Human(Discord, AccountLinkKeys.DiscordUserType);

        var link = await this._sut.LinkAsync(Telegram, LinkSide.Discord, Discord, "Northside");

        Assert.Equal(LinkSide.Telegram, link.DefaultDestination);
        Assert.Equal(Discord, link.DiscordId);
        Assert.Equal(Telegram, link.TelegramId);
    }

    [Fact]
    public async Task Link_SameTypeTwice_WrongType()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Discord, "100000000000000002", "x"));

        Assert.Equal(LinkRefusalCode.WrongType, ex.Code);
        Assert.Equal("link_wrong_type", ex.WireCode);
    }

    [Fact]
    public async Task Link_DestinationCaller_WrongType()
    {
        this.Human("500", "discord:channel");

        var ex = await Refused(this._sut.LinkAsync("500", LinkSide.Telegram, Telegram, "x"));

        Assert.Equal(LinkRefusalCode.WrongType, ex.Code);
    }

    [Fact]
    public async Task Link_NegativeTelegramId_BadId()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, "-200000001", "x"));

        Assert.Equal(LinkRefusalCode.BadId, ex.Code);
    }

    [Fact]
    public async Task Link_DiscordIdWithLetters_BadId()
    {
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);

        var ex = await Refused(this._sut.LinkAsync(Telegram, LinkSide.Discord, "10000000000000000a", "x"));

        Assert.Equal(LinkRefusalCode.BadId, ex.Code);
    }

    [Fact]
    public async Task Link_TwentyOneDigitDiscordId_BadId_AndTwentyDigitsPass()
    {
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);
        this.Human("12345678901234567890", AccountLinkKeys.DiscordUserType);

        var ex = await Refused(this._sut.LinkAsync(Telegram, LinkSide.Discord, "123456789012345678901", "x"));
        Assert.Equal(LinkRefusalCode.BadId, ex.Code);

        var link = await this._sut.LinkAsync(Telegram, LinkSide.Discord, "12345678901234567890", "x");
        Assert.Equal("12345678901234567890", link.DiscordId);
    }

    [Fact]
    public async Task Link_ZeroTelegramId_BadId()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, "00000000000000000000", "x"));

        Assert.Equal(LinkRefusalCode.BadId, ex.Code);
    }

    [Theory]
    [InlineData("0123")]
    [InlineData("0")]
    public async Task Link_LeadingZero_BadId(string id)
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, id, "x"));

        Assert.Equal(LinkRefusalCode.BadId, ex.Code);
    }

    [Fact]
    public async Task Link_IdWithInteriorZeros_Accepted()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Human("200000010", AccountLinkKeys.TelegramUserType);

        var link = await this._sut.LinkAsync(Discord, LinkSide.Telegram, "200000010", "x");

        Assert.Equal("200000010", link.TelegramId);
    }

    [Fact]
    public async Task Link_SaveFailsAfterProvisioning_RollsBackAndRethrows()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        this._links.Setup(r => r.AddAsync(It.IsAny<AccountLink>())).ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town"));

        this._provisioner.Verify(p => p.RollBackAsync(Telegram, LinkSide.Telegram, "Old Town"), Times.Once);
    }

    [Fact]
    public async Task Link_InvalidationThrowing_NeverRollsBackASavedLink()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        this._linked.Setup(l => l.Invalidate(It.IsAny<string>())).Throws(new InvalidOperationException("cache"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town"));

        this._provisioner.Verify(p => p.RollBackAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Link_ExistingOtherIsGroup_TypeMismatch()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Human(Telegram, "telegram:group");

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "x"));

        Assert.Equal(LinkRefusalCode.TypeMismatch, ex.Code);
        this._links.Verify(r => r.AddAsync(It.IsAny<AccountLink>()), Times.Never);
    }

    [Fact]
    public async Task Link_AlreadyLinked_TakenBeforeAnythingIsCreated()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        this._links.Setup(r => r.GetByHumanIdAsync(Telegram))
            .ReturnsAsync(new AccountLink(1, "100000000000000009", Telegram, LinkSide.Discord, null, null, DateTime.UtcNow));

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "x"));

        Assert.Equal(LinkRefusalCode.Taken, ex.Code);
        this._provisioner.Verify(p => p.CreateMirrorAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Link_UniqueViolationRace_MapsToLinkTaken()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);
        this._links.Setup(r => r.AddAsync(It.IsAny<AccountLink>()))
            .ThrowsAsync(new AccountLinkConflictException("dup", LinkSide.Telegram));

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "x"));

        Assert.Equal(LinkRefusalCode.Taken, ex.Code);
        this._provisioner.Verify(p => p.RollBackAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Link_RaceAfterProvisioning_RemovesWhatItCreated()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        this._links.Setup(r => r.AddAsync(It.IsAny<AccountLink>())).ThrowsAsync(new AccountLinkConflictException("dup"));

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town"));

        Assert.Equal(LinkRefusalCode.Taken, ex.Code);
        this._provisioner.Verify(p => p.RollBackAsync(Telegram, LinkSide.Telegram, "Old Town"), Times.Once);
    }

    [Fact]
    public async Task Link_OtherMissing_SettingOff_SavesLinkWithoutCreate()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(false);

        var link = await this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town");

        Assert.Equal(Telegram, link.TelegramId);
        this._provisioner.Verify(p => p.CreateMirrorAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Link_OtherMissing_SettingOn_ProvisionsThenSaves()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        var order = new List<string>();
        this._provisioner.Setup(p => p.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "Old Town"))
            .Callback(() => order.Add("create")).Returns(Task.CompletedTask);
        this._links.Setup(r => r.AddAsync(It.IsAny<AccountLink>()))
            .Callback(() => order.Add("save")).ReturnsAsync((AccountLink l) => l);

        await this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "Old Town");

        Assert.Equal(["create", "save"], order);
    }

    [Fact]
    public async Task Link_ProvisionFails_NothingSaved()
    {
        this.Human(Discord, AccountLinkKeys.DiscordUserType);
        this.Missing(Telegram);
        this._settings.Setup(s => s.GetBoolAsync(AccountLinkKeys.RegisterMissing)).ReturnsAsync(true);
        this._provisioner.Setup(p => p.CreateMirrorAsync(Discord, LinkSide.Telegram, Telegram, "x"))
            .ThrowsAsync(new LinkRefusalException(LinkRefusalCode.ProvisionFailed, "Nothing was linked."));

        var ex = await Refused(this._sut.LinkAsync(Discord, LinkSide.Telegram, Telegram, "x"));

        Assert.Equal("link_provision_failed", ex.WireCode);
        this._links.Verify(r => r.AddAsync(It.IsAny<AccountLink>()), Times.Never);
    }

    [Fact]
    public async Task Unlink_InvalidatesBothIds()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Telegram))
            .ReturnsAsync(new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow));
        this._links.Setup(r => r.RemoveByHumanIdAsync(Telegram)).ReturnsAsync(1);

        Assert.True(await this._sut.UnlinkAsync(Telegram));

        this._linked.Verify(l => l.Invalidate(Discord), Times.Once);
        this._linked.Verify(l => l.Invalidate(Telegram), Times.Once);
    }

    [Fact]
    public async Task Unlink_NoLink_FalseAndNoInvalidation()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Telegram)).ReturnsAsync((AccountLink?)null);

        Assert.False(await this._sut.UnlinkAsync(Telegram));

        this._linked.Verify(l => l.Invalidate(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetDefault_SideMissing_Refused()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord))
            .ReturnsAsync(new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow));
        this.Missing(Telegram);

        var ex = await Refused(this._sut.SetDefaultAsync(Discord, LinkSide.Telegram));

        Assert.Equal(LinkRefusalCode.NotPermitted, ex.Code);
        this._links.Verify(r => r.SetDefaultAsync(It.IsAny<int>(), It.IsAny<LinkSide>()), Times.Never);
    }

    [Fact]
    public async Task SetDefault_SideExists_SavesAndInvalidatesBoth()
    {
        this._links.Setup(r => r.GetByHumanIdAsync(Discord))
            .ReturnsAsync(new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow));
        this.Human(Telegram, AccountLinkKeys.TelegramUserType);

        await this._sut.SetDefaultAsync(Discord, LinkSide.Telegram);

        this._links.Verify(r => r.SetDefaultAsync(1, LinkSide.Telegram), Times.Once);
        this._linked.Verify(l => l.Invalidate(Discord), Times.Once);
        this._linked.Verify(l => l.Invalidate(Telegram), Times.Once);
    }
}
