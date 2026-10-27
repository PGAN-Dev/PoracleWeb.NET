using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// <c>POST api/{type}/batch</c>: an add dialog's multi-select as one request (#1090). The legitimate cases
/// (one Deliver to choice on every row, a full Pokédex) must still pass beside the refusals.
/// </summary>
public class BatchCreateControllerTests : ControllerTestBase
{
    private const string User = "123456789";

    private readonly Mock<IMonsterService> _monsters = new();
    private readonly MonsterController _sut;

    public BatchCreateControllerTests()
    {
        this._sut = new MonsterController(this._monsters.Object, new Mock<IPokemonEditCapabilityService>().Object, UnlinkedRouter());
        SetupUser(this._sut);
        this._monsters.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Monster>>()))
            .ReturnsAsync((string _, IReadOnlyList<Monster> rows) =>
                new BatchCreateResult<Monster>([.. rows.Select((m, i) => { m.Uid = i == 0 ? 0 : 100 + i; return m; })], 0, null));
    }

    private static T? Field<T>(object? value, string name) => (T?)value?.GetType().GetProperty(name)?.GetValue(value);

    [Fact]
    public async Task MonsterBatchIsOneServiceCallAnsweringEveryRowInOrder()
    {
        var result = await this._sut.CreateBatch([new() { PokemonId = 1 }, new() { PokemonId = 2 }, new() { PokemonId = 3 }]);

        var ok = Assert.IsType<OkObjectResult>(result);
        var alarms = Field<IReadOnlyList<Monster>>(ok.Value, "alarms");
        Assert.Equal([1, 2, 3], alarms!.Select(m => m.PokemonId));
        Assert.Equal([0, 101, 102], alarms!.Select(m => m.Uid));
        Assert.Equal(0, Field<int>(ok.Value, "takeoversSkipped"));
        this._monsters.Verify(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Monster>>()), Times.Once);
        this._monsters.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<Monster>()), Times.Never);
    }

    [Fact]
    public async Task MonsterBatchOfEverySpeciesWithOneDeliverToChoicePasses()
    {
        List<MonsterCreate> rows = [.. Enumerable.Range(1, 1025).Select(id => new MonsterCreate { PokemonId = id, Destination = "telegram" })];

        var result = await this._sut.CreateBatch(rows);

        Assert.IsType<OkObjectResult>(result);
        this._monsters.Verify(s => s.CreateBatchAsync(User, It.Is<IReadOnlyList<Monster>>(m => m.Count == 1025)), Times.Once);
    }

    [Fact]
    public async Task MonsterBatchReportsTakeoversItLeftOut()
    {
        this._monsters.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Monster>>()))
            .ReturnsAsync((string _, IReadOnlyList<Monster> rows) => new BatchCreateResult<Monster>(rows, 1, "in the way"));

        var result = await this._sut.CreateBatch([new() { PokemonId = 1 }, new() { PokemonId = 2 }]);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, Field<int>(ok.Value, "takeoversSkipped"));
        Assert.Equal("in the way", Field<string>(ok.Value, "takeoverReason"));
    }

    [Fact]
    public async Task MonsterBatchRefusesAnEmptyList()
    {
        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateBatch([]));
        this._monsters.Verify(s => s.CreateBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Monster>>()), Times.Never);
    }

    [Fact]
    public async Task MonsterBatchRefusesMoreThanTheCap()
    {
        List<MonsterCreate> rows = [.. Enumerable.Range(0, 5001).Select(_ => new MonsterCreate { PokemonId = 1 })];

        Assert.IsType<BadRequestObjectResult>(await this._sut.CreateBatch(rows));
        this._monsters.Verify(s => s.CreateBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Monster>>()), Times.Never);
    }

    [Fact]
    public async Task MonsterBatchRefusesRowsForDifferentDestinations()
    {
        var result = await this._sut.CreateBatch([new() { PokemonId = 1, Destination = "discord" }, new() { PokemonId = 2, Destination = "telegram" }]);

        Assert.IsType<BadRequestObjectResult>(result);
        this._monsters.Verify(s => s.CreateBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Monster>>()), Times.Never);
    }

    [Fact]
    public async Task MonsterBatchRefusesAnInvertedRangeOnAnyRow()
    {
        var result = await this._sut.CreateBatch([new() { PokemonId = 1 }, new() { PokemonId = 2, MinIv = 90, MaxIv = 10 }]);

        Assert.IsType<BadRequestObjectResult>(result);
        this._monsters.Verify(s => s.CreateBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Monster>>()), Times.Never);
    }

    [Fact]
    public async Task RaidBatchIsOneServiceCall()
    {
        var service = new Mock<IRaidService>();
        service.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Raid>>()))
            .ReturnsAsync((string _, IReadOnlyList<Raid> rows) => new BatchCreateResult<Raid>(rows, 0, null));
        var sut = new RaidController(service.Object, UnlinkedRouter());
        SetupUser(sut);

        Assert.IsType<OkObjectResult>(await sut.CreateBatch([new() { PokemonId = 150 }, new() { PokemonId = 151 }]));
        service.Verify(s => s.CreateBatchAsync(User, It.Is<IReadOnlyList<Raid>>(r => r.Count == 2)), Times.Once);
    }

    [Fact]
    public async Task MaxBattleBatchIsOneServiceCall()
    {
        var service = new Mock<IMaxBattleService>();
        service.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<MaxBattle>>()))
            .ReturnsAsync((string _, IReadOnlyList<MaxBattle> rows) => new BatchCreateResult<MaxBattle>(rows, 0, null));
        var sut = new MaxBattleController(service.Object, UnlinkedRouter());
        SetupUser(sut);

        Assert.IsType<OkObjectResult>(await sut.CreateBatch([new() { PokemonId = 1 }, new() { PokemonId = 4 }]));
        service.Verify(s => s.CreateBatchAsync(User, It.Is<IReadOnlyList<MaxBattle>>(r => r.Count == 2)), Times.Once);
    }

    [Fact]
    public async Task NestBatchIsOneServiceCall()
    {
        var service = new Mock<INestService>();
        service.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Nest>>()))
            .ReturnsAsync((string _, IReadOnlyList<Nest> rows) => new BatchCreateResult<Nest>(rows, 0, null));
        var sut = new NestController(service.Object, UnlinkedRouter());
        SetupUser(sut);

        Assert.IsType<OkObjectResult>(await sut.CreateBatch([new() { PokemonId = 1 }, new() { PokemonId = 4 }]));
        service.Verify(s => s.CreateBatchAsync(User, It.Is<IReadOnlyList<Nest>>(r => r.Count == 2)), Times.Once);
    }

    [Fact]
    public async Task QuestBatchIsOneServiceCall()
    {
        var service = new Mock<IQuestService>();
        service.Setup(s => s.CreateBatchAsync(User, It.IsAny<IReadOnlyList<Quest>>()))
            .ReturnsAsync((string _, IReadOnlyList<Quest> rows) => new BatchCreateResult<Quest>(rows, 0, null));
        var sut = new QuestController(service.Object, UnlinkedRouter());
        SetupUser(sut);

        Assert.IsType<OkObjectResult>(await sut.CreateBatch([new() { RewardType = 7, Reward = 1 }, new() { RewardType = 7, Reward = 4 }]));
        service.Verify(s => s.CreateBatchAsync(User, It.Is<IReadOnlyList<Quest>>(r => r.Count == 2)), Times.Once);
    }
}
