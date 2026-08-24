using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Pokecoin quest rewards against a PoracleNG that has them and one that does not.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG widened <c>validRewardTypes</c> from <c>{2,3,4,7,12}</c> to include <c>8</c> on its
/// develop line. An older server answers 400 "Unrecognised reward_type value", which reaches the user
/// as an unexplained failure, so PoracleWeb refuses first and says which version would be needed.
/// </para>
/// <para>
/// Half of these tests exist to prove the guard did <em>not</em> catch anything else. Tightening a rule
/// without enumerating who depended on the loose one is the failure that produced #626, #637 and #553;
/// here the loose rule permitted five reward types that every supported PoracleNG accepts, and
/// <see cref="EveryOtherRewardTypeStillPassesOnTheReleasedLine"/> names all five.
/// </para>
/// </remarks>
public class QuestPokecoinsCapabilityTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _uidRemapper = new();

    public QuestPokecoinsCapabilityTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync("quest", It.IsAny<string>()))
            .ReturnsAsync(JsonDocument.Parse("[]").RootElement);
        this._proxy.Setup(p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 0, 0));
    }

    /// <summary>
    /// The five reward types that predate the split must be unaffected by the guard, on the branch that
    /// has no pokecoin support at all. Without this, a guard that refused every quest would pass the
    /// refusal test above and nobody would notice until the release.
    /// </summary>
    [Theory]
    [InlineData(QuestRewardTypes.Item)]
    [InlineData(QuestRewardTypes.Stardust)]
    [InlineData(QuestRewardTypes.Candy)]
    [InlineData(QuestRewardTypes.Pokemon)]
    [InlineData(QuestRewardTypes.MegaEnergy)]
    public async Task EveryOtherRewardTypeStillPassesOnTheReleasedLine(int rewardType)
    {
        var sut = this.Build(PoracleCapabilityStub.None);

        var created = await sut.CreateAsync("u1", new Quest { RewardType = rewardType, Reward = 1 });

        Assert.Equal(rewardType, created.RewardType);
        this._proxy.Verify(p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task CreateRefusesPokecoinsWhenTheServerCannotStoreThem()
    {
        var sut = this.Build(PoracleCapabilityStub.None);

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.CreateAsync("u1", new Quest { RewardType = QuestRewardTypes.Pokecoins, Reward = 10 }));

        Assert.Equal(PoracleCapabilityKeys.QuestPokecoins, ex.Capability);
        // Refused before the write, not after: a rejected create must not reach PoracleNG at all.
        this._proxy.Verify(
            p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAcceptsPokecoinsWhenTheServerHasThem()
    {
        var sut = this.Build(PoracleCapabilityStub.Supporting(PoracleCapabilityKeys.QuestPokecoins));

        var created = await sut.CreateAsync("u1", new Quest { RewardType = QuestRewardTypes.Pokecoins, Reward = 10 });

        Assert.Equal(QuestRewardTypes.Pokecoins, created.RewardType);
        this._proxy.Verify(p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRefusesPokecoinsWhenTheServerCannotStoreThem()
    {
        var sut = this.Build(PoracleCapabilityStub.None);

        await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => sut.UpdateAsync("u1", new Quest { Uid = 4, RewardType = QuestRewardTypes.Pokecoins, Reward = 10 }));
    }

    /// <summary>
    /// The bulk path is where profile import and quick-pick apply arrive, and it is the one that was
    /// left behind last time a sibling was hardened (#641). A pokecoin row anywhere in the batch has to
    /// be caught, not just the first — PoracleNG refuses the whole POST if any row is bad.
    /// </summary>
    [Fact]
    public async Task BulkCreateRefusesAPokecoinRowAnywhereInTheBatch()
    {
        var sut = this.Build(PoracleCapabilityStub.None);

        var batch = new[]
        {
            new Quest { RewardType = QuestRewardTypes.Pokemon, Reward = 25 },
            new Quest { RewardType = QuestRewardTypes.Item, Reward = 1 },
            new Quest { RewardType = QuestRewardTypes.Pokecoins, Reward = 10 },
        };

        await Assert.ThrowsAsync<PoracleUnsupportedException>(() => sut.BulkCreateAsync("u1", batch));

        this._proxy.Verify(
            p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()),
            Times.Never);
    }

    /// <summary>A batch of ordinary rewards must still go through in one call on the released line.</summary>
    [Fact]
    public async Task BulkCreateStillAcceptsAnOrdinaryBatchOnTheReleasedLine()
    {
        var sut = this.Build(PoracleCapabilityStub.None);

        var batch = new[]
        {
            new Quest { RewardType = QuestRewardTypes.Pokemon, Reward = 25 },
            new Quest { RewardType = QuestRewardTypes.Stardust, Reward = 1000 },
        };

        var created = await sut.BulkCreateAsync("u1", batch);

        Assert.Equal(2, created.Count());
        this._proxy.Verify(p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    private QuestService Build(IPoracleCapabilityService capabilities) => new(
        this._proxy.Object,
        this._featureGate.Object,
        capabilities,
        NullLogger<QuestService>.Instance,
        this._uidRemapper.Object);
}
