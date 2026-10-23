using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class QuickPickUidTransferTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IQuickPickAppliedStateRepository> _repo = new();
    private readonly QuickPickUidTransfer _sut;

    public QuickPickUidTransferTests() =>
        this._sut = new QuickPickUidTransfer(this._repo.Object, NullLogger<QuickPickUidTransfer>.Instance);

    private static QuickPickAppliedState State(string user, string pick, string type, params int[] uids) => new()
    {
        UserId = user,
        ProfileNo = 1,
        QuickPickId = pick,
        AlarmType = type,
        ExcludePokemonIds = [7],
        TrackedUids = [.. uids]
    };

    [Fact]
    public async Task UidLeavesTheSourceRowAndLandsInTheTargetRowOfTheSamePick()
    {
        var source = State(Discord, "lure-glacial", "lure", 10, 11);
        var target = State(Telegram, "lure-glacial", "lure", 99);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync([source]);
        this._repo.Setup(r => r.GetAsync(Telegram, 1, "lure-glacial")).ReturnsAsync(target);

        await this._sut.TransferAsync(Discord, Telegram, 1, "lure", 10, 500);

        Assert.Equal([11], source.TrackedUids);
        Assert.Equal([99, 500], target.TrackedUids);
        this._repo.Verify(r => r.CreateOrUpdateAsync(source), Times.Once);
        this._repo.Verify(r => r.CreateOrUpdateAsync(target), Times.Once);
    }

    [Fact]
    public async Task CreatesTheTargetRowWhenAbsent()
    {
        var source = State(Discord, "lure-glacial", "lure", 10);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync([source]);
        this._repo.Setup(r => r.GetAsync(Telegram, 1, "lure-glacial")).ReturnsAsync((QuickPickAppliedState?)null);

        await this._sut.TransferAsync(Discord, Telegram, 1, "lure", 10, 500);

        this._repo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickAppliedState>(s =>
            s.UserId == Telegram && s.ProfileNo == 1 && s.QuickPickId == "lure-glacial" && s.AlarmType == "lure"
            && s.TrackedUids.SequenceEqual(new[] { 500 }) && s.ExcludePokemonIds.SequenceEqual(new[] { 7 }))), Times.Once);
    }

    [Fact]
    public async Task PokemonMapsToTheMonsterAppliedType()
    {
        var source = State(Discord, "great-league", "monster", 10);
        var other = State(Discord, "other", "raid", 10);
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync([other, source]);
        this._repo.Setup(r => r.GetAsync(Telegram, 1, "great-league")).ReturnsAsync((QuickPickAppliedState?)null);

        await this._sut.TransferAsync(Discord, Telegram, 1, "pokemon", 10, 500);

        Assert.Empty(source.TrackedUids);
        Assert.Equal([10], other.TrackedUids);
        this._repo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickAppliedState>(s => s.UserId == Telegram && s.AlarmType == "monster")), Times.Once);
    }

    [Fact]
    public async Task AUidInNoPickIsANoOp()
    {
        this._repo.Setup(r => r.GetByUserAndProfileAsync(Discord, 1)).ReturnsAsync([State(Discord, "p", "lure", 11)]);

        await this._sut.TransferAsync(Discord, Telegram, 1, "lure", 10, 500);

        this._repo.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickAppliedState>()), Times.Never);
    }
}
