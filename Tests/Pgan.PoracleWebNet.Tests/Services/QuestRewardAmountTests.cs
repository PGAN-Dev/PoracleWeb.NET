using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// "At least three of them", and the one reward type that expresses its floor somewhere else.
/// </summary>
/// <remarks>
/// PoracleNG compares <c>amount</c> against the quantity for items, candy and mega energy. Stardust is
/// the exception: <c>singleRewardMatches</c> reads <c>reward</c> as the dust floor for reward type 3 and
/// ignores <c>amount</c> entirely, which is why the stardust rule carries its number in a different
/// field from every other reward tab.
/// </remarks>
public class QuestRewardAmountTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();
    private JsonElement _sent;

    public QuestRewardAmountTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._remapper
            .Setup(r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        this._proxy
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sent = body.Clone())
            .ReturnsAsync(new TrackingCreateResult([1], 0, 0, 1));
    }

    private async Task<JsonElement> WriteAsync(QuestCreate create)
    {
        await new QuestService(
                this._proxy.Object, this._featureGate.Object, NullLogger<QuestService>.Instance, this._remapper.Object)
            .CreateAsync("u1", create.ToQuest());

        return this._sent.ValueKind == JsonValueKind.Array ? this._sent.EnumerateArray().First() : this._sent;
    }

    [Fact]
    public async Task AnItemRuleCarriesItsMinimumToPoracleNg()
    {
        // Bound from JSON because that is the DTO the controller binds; constructing the domain model
        // skips both the binding and the validation attributes (#548, #555, #565).
        var create = JsonSerializer.Deserialize<QuestCreate>(
            """{"reward":1301,"rewardType":2,"amount":3}""", Web)!;

        Assert.Equal(3, (await this.WriteAsync(create)).GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task ARuleWithNoMinimumAsksForNone()
    {
        var create = JsonSerializer.Deserialize<QuestCreate>("""{"reward":25,"rewardType":7}""", Web)!;

        Assert.Equal(0, (await this.WriteAsync(create)).GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task AStardustRuleKeepsItsFloorInRewardWhereMatchingLooksForIt()
    {
        var create = JsonSerializer.Deserialize<QuestCreate>(
            """{"reward":1500,"rewardType":3,"amount":0}""", Web)!;

        var row = await this.WriteAsync(create);

        Assert.Equal(1500, row.GetProperty("reward").GetInt32());
        Assert.Equal(0, row.GetProperty("amount").GetInt32());
    }

    [Fact]
    public void AnEditThatSaysNothingAboutTheMinimumLeavesItAlone()
    {
        var existing = new Quest { Uid = 7, Reward = 1301, RewardType = 2, Amount = 3 };

        new QuestUpdate { Distance = 1000 }.ApplyUpdate(existing);

        Assert.Equal(3, existing.Amount);
    }
}
