using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Mirrors what PoracleNG's <c>/poracle</c> admits (telegrambot/reconciliation.go): a member of any group in
/// <c>telegram.channels</c> without area security, and with it the communities whose own groups hold the person. See #1056.
/// </summary>
public class TelegramRegistrationCheckTests
{
    private const string Person = "200000001";

    private readonly Mock<IPoracleApiProxy> _config = new();
    private readonly Mock<ITelegramMembershipClient> _telegram = new();
    private readonly TelegramRegistrationCheck _sut;

    public TelegramRegistrationCheckTests() =>
        this._sut = new TelegramRegistrationCheck(this._config.Object, this._telegram.Object, NullLogger<TelegramRegistrationCheck>.Instance);

    private void Groups(bool areaSecurity, string[] channels, params (string Name, string[] Channels)[] communities) =>
        this._config.Setup(c => c.GetTelegramRegistrationGroupsAsync()).ReturnsAsync(new TelegramRegistrationGroups(
            areaSecurity, channels, communities.Select(c => new TelegramCommunityGroups(c.Name, c.Channels)).ToList()));

    private void MemberOf(params long[] chats) =>
        this._telegram.Setup(t => t.IsMemberAsync(It.IsAny<long>(), 200000001, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long chat, long _, CancellationToken _) => chats.Contains(chat));

    [Fact]
    public async Task WithoutAreaSecurity_AMemberOfAnyGroupQualifies_AndNoCommunityIsRecorded()
    {
        this.Groups(false, ["-1001", "-1002"]);
        this.MemberOf(-1002);

        var verdict = await this._sut.CheckAsync(Person);

        Assert.True(verdict.Qualified);
        Assert.Empty(verdict.Communities);
    }

    [Fact]
    public async Task WithoutAreaSecurity_ACommunitysGroupsAreIgnored()
    {
        this.Groups(false, ["-1001"], ("north", ["-1003"]));
        this.MemberOf(-1003);

        Assert.False((await this._sut.CheckAsync(Person)).Qualified);
    }

    [Fact]
    public async Task NotInAnyGroup_DoesNotQualify()
    {
        this.Groups(false, ["-1001", "-1002"]);
        this.MemberOf();

        Assert.False((await this._sut.CheckAsync(Person)).Qualified);
    }

    [Fact]
    public async Task WithAreaSecurity_RecordsEveryCommunityTheyAreIn_AndOnlyThose()
    {
        this.Groups(true, ["-1001"], ("north", ["-1003", "-1004"]), ("river", ["-1005"]), ("hill", ["-1006"]));
        this.MemberOf(-1004, -1006);

        var verdict = await this._sut.CheckAsync(Person);

        Assert.True(verdict.Qualified);
        Assert.Equal(["north", "hill"], verdict.Communities);
    }

    [Fact]
    public async Task WithAreaSecurity_TheTopLevelChannelsDoNotQualify()
    {
        this.Groups(true, ["-1001"], ("north", ["-1003"]));
        this.MemberOf(-1001);

        Assert.False((await this._sut.CheckAsync(Person)).Qualified);
    }

    [Fact]
    public async Task AGroupSharedByTwoCommunities_IsAskedOnce_AndCountsForBoth()
    {
        this.Groups(true, [], ("north", ["-1003"]), ("river", ["-1003"]));
        this.MemberOf(-1003);

        var verdict = await this._sut.CheckAsync(Person);

        Assert.Equal(["north", "river"], verdict.Communities);
        this._telegram.Verify(t => t.IsMemberAsync(-1003, 200000001, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChannelsThatAreNotNumbers_AreSkippedLikePoracleNGDoes()
    {
        this.Groups(false, ["@somechannel", "", "-1002"]);
        this.MemberOf(-1002);

        Assert.True((await this._sut.CheckAsync(Person)).Qualified);
        this._telegram.Verify(t => t.IsMemberAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoGroupsConfigured_DoesNotQualify_AndAsksTelegramNothing()
    {
        this.Groups(false, []);

        Assert.False((await this._sut.CheckAsync(Person)).Qualified);
        this._telegram.Verify(t => t.IsMemberAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PoracleNGUnreadable_DoesNotQualify_AndDoesNotThrow()
    {
        this._config.Setup(c => c.GetTelegramRegistrationGroupsAsync()).ThrowsAsync(new HttpRequestException("503"));

        Assert.False((await this._sut.CheckAsync(Person)).Qualified);
    }

    [Fact]
    public async Task OneGroupFailing_DoesNotStopTheNextOneFromQualifying()
    {
        this.Groups(false, ["-1001", "-1002"]);
        this._telegram.Setup(t => t.IsMemberAsync(-1001, 200000001, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        this._telegram.Setup(t => t.IsMemberAsync(-1002, 200000001, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True((await this._sut.CheckAsync(Person)).Qualified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public async Task ANonNumericPerson_DoesNotQualify(string id)
    {
        this.Groups(false, ["-1001"]);

        Assert.False((await this._sut.CheckAsync(id)).Qualified);
        this._telegram.Verify(t => t.IsMemberAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
