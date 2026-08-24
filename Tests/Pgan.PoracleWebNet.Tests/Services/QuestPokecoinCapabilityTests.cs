using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.TestDoubles;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Pokecoin quest rewards (<c>reward_type: 8</c>) reach PoracleNG only when it can store them.
/// </summary>
/// <remarks>
/// Verified by calling both dev servers directly rather than by reading upstream source: 5.1.0 answers
/// 400 "Unrecognised reward_type value" and 5.2.1 stores the row. Every refusal below is paired with
/// the legitimate case that must keep working -- an old server still has to take stardust, and a new
/// one still has to take pokecoins -- because a gate that refused everything would pass a refusal-only
/// suite.
/// </remarks>
public class QuestPokecoinCapabilityTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public QuestPokecoinCapabilityTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._proxy.Setup(p => p.GetByUserAsync("quest", It.IsAny<string>())).ReturnsAsync(EmptyArray());
        this._proxy.Setup(p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([7], 0, 0, 1));
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CreateRefusesPokecoinsWhenTheServerCannotStoreThem()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => sut.CreateAsync("u1", Pokecoins()));

        Assert.Contains("5.2.0", ex.Message, StringComparison.Ordinal);
        this._proxy.Verify(
            p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task CreateAcceptsPokecoinsWhenTheServerCanStoreThem()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Supported);

        var result = await sut.CreateAsync("u1", Pokecoins());

        Assert.Equal(7, result.Uid);
        this._proxy.Verify(
            p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>The five reward types every supported PoracleNG takes must not be caught by the gate.</summary>
    [Theory]
    [InlineData(QuestRewardTypes.Item)]
    [InlineData(QuestRewardTypes.Stardust)]
    [InlineData(QuestRewardTypes.Candy)]
    [InlineData(QuestRewardTypes.Pokemon)]
    [InlineData(QuestRewardTypes.MegaEnergy)]
    public async Task CreateStillAcceptsEveryUngatedRewardTypeOnAnOlderServer(int rewardType)
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        var result = await sut.CreateAsync("u1", new Quest
        {
            Reward = 500,
            RewardType = rewardType
        });

        Assert.Equal(7, result.Uid);
    }

    [Fact]
    public async Task UpdateRefusesPokecoinsWhenTheServerCannotStoreThem()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        await Assert.ThrowsAsync<AlarmValidationException>(
            () => sut.UpdateAsync("u1", Pokecoins()));

        this._proxy.Verify(
            p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    /// <summary>
    /// Bulk is the path quick-pick apply and profile import take, and neither passes a quest action, so
    /// a controller-level gate would miss both. Every distinct reward type in the batch is checked, not
    /// just the first: PoracleNG refuses the whole POST when any row is bad.
    /// </summary>
    [Fact]
    public async Task BulkCreateRefusesABatchWhosePokecoinRowIsNotTheFirst()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        await Assert.ThrowsAsync<AlarmValidationException>(() => sut.BulkCreateAsync("u1", [
            new Quest
            {
                Reward = 500,
                RewardType = QuestRewardTypes.Stardust
            },
            Pokecoins(),
        ]));

        this._proxy.Verify(
            p => p.CreateAsync("quest", It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateStillWritesABatchWithNoPokecoinRowOnAnOlderServer()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        var results = await sut.BulkCreateAsync("u1", [
            new Quest
            {
                Reward = 500,
                RewardType = QuestRewardTypes.Stardust
            },
            new Quest
            {
                Reward = 25,
                RewardType = QuestRewardTypes.Pokemon
            },
        ]);

        Assert.Equal(2, results.Count());
        this._proxy.Verify(
            p => p.CreateAsync("quest", "u1", It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// Reading is never gated. A pokecoin rule set with the bot, or left behind by a downgrade, has to
    /// stay visible -- a row nobody can see is a row nobody can delete.
    /// </summary>
    [Fact]
    public async Task AnExistingPokecoinRuleIsStillReadableOnAnOlderServer()
    {
        var stored = JsonSerializer.Deserialize<JsonElement>(
            """[{"uid":519,"id":"u1","reward_type":8,"reward":50}]""");
        this._proxy.Setup(p => p.GetByUserAsync("quest", "u1")).ReturnsAsync(stored);

        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        var quest = await sut.GetByUidAsync("u1", 519);

        Assert.NotNull(quest);
        Assert.Equal(QuestRewardTypes.Pokecoins, quest.RewardType);
    }

    /// <summary>Deleting one is not gated either, for the same reason.</summary>
    [Fact]
    public async Task AnExistingPokecoinRuleIsStillDeletableOnAnOlderServer()
    {
        var sut = this.Sut(PokecoinCapabilityStub.Unsupported);

        await sut.DeleteAsync("u1", 519);

        this._proxy.Verify(p => p.DeleteByUidAsync("quest", "u1", 519), Times.Once);
    }

    [Fact]
    public async Task TheControllerReportsWhetherPokecoinsAreAvailable()
    {
        var controller = new QuestController(new Mock<IQuestService>().Object, PokecoinCapabilityStub.Supported);

        var result = Assert.IsType<OkObjectResult>(await controller.GetCapability());

        Assert.True((bool)result.Value!.GetType().GetProperty("pokecoins")!.GetValue(result.Value)!);
    }

    [Fact]
    public async Task TheControllerReportsPokecoinsUnavailableOnAnOlderServer()
    {
        var controller = new QuestController(new Mock<IQuestService>().Object, PokecoinCapabilityStub.Unsupported);

        var result = Assert.IsType<OkObjectResult>(await controller.GetCapability());

        Assert.False((bool)result.Value!.GetType().GetProperty("pokecoins")!.GetValue(result.Value)!);
    }

    private static Quest Pokecoins() => new()
    {
        // PoracleNG matches pokecoins on the amount alone and reads it from reward, as it does stardust.
        Reward = 50,
        RewardType = QuestRewardTypes.Pokecoins
    };

    private static JsonElement EmptyArray() => JsonSerializer.Deserialize<JsonElement>("[]");

    private QuestService Sut(IQuestPokecoinCapabilityService capability) => new(
        this._proxy.Object,
        this._featureGate.Object,
        capability,
        NullLogger<QuestService>.Instance,
        this._remapper.Object);
}
