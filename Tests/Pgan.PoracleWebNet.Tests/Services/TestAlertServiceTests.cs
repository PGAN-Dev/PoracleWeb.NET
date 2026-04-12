using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Core.Services.Pvp;
using Pgan.PoracleWebNet.Core.Services.TestAlerts;

namespace Pgan.PoracleWebNet.Tests.Services;

public class TestAlertServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleApiProxy> _apiProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _trackingProxy = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IMasterDataService> _masterData = new();
    private readonly Mock<ILogger<TestAlertService>> _logger = new();
    private readonly TestAlertService _sut;

    public TestAlertServiceTests()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var pvpService = new PvpRankService(cache);
        var builders = new ITestPayloadBuilder[]
        {
            new PokemonTestPayloadBuilder(pvpService, this._masterData.Object),
            new RaidOrEggTestPayloadBuilder(),
            new QuestTestPayloadBuilder(),
            new PokestopTestPayloadBuilder(),
            new NestTestPayloadBuilder(),
            new GymTestPayloadBuilder(),
        };

        this._sut = new TestAlertService(
            this._apiProxy.Object,
            this._trackingProxy.Object,
            this._humanProxy.Object,
            builders,
            this._logger.Object);
    }

    [Fact]
    public async Task SendTestAlertAsyncValidPokemonSendsRequest()
    {
        var alarms = CreateJsonArray(new
        {
            uid = 42,
            pokemon_id = 25,
            form = 0,
            template = "default"
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TestUser",
            type = "discord:user",
            language = "en",
            latitude = 40.7128,
            longitude = -74.006
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", "pokemon", 42);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Type == "pokemon" &&
            r.Target.Id == "user1" &&
            r.Target.Name == "TestUser" &&
            r.Target.Type == "discord:user" &&
            r.Target.Language == "en"
        )), Times.Once);
    }

    [Fact]
    public async Task SendTestAlertAsyncTelegramUserSetsTargetTypeTelegram()
    {
        var alarms = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 25
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TelegramUser",
            type = "telegram:user",
            language = "en",
            latitude = 0.0,
            longitude = 0.0
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", "pokemon", 1);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Target.Type == "telegram:user"
        )), Times.Once);
    }

    [Theory]
    [InlineData("raid", "raid")]
    [InlineData("egg", "raid")]
    [InlineData("quest", "quest")]
    [InlineData("invasion", "pokestop")]
    [InlineData("lure", "pokestop")]
    [InlineData("gym", "gym")]
    public async Task SendTestAlertAsyncAllValidTypesSendsRequest(string alarmType, string expectedWireType)
    {
        var alarms = CreateJsonArray(new
        {
            uid = 10,
            pokemon_id = 150
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync(alarmType, "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TestUser",
            type = "discord:user"
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", alarmType, 10);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Type == expectedWireType
        )), Times.Once);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task SendTestAlertAsyncInvalidTypeThrowsArgumentException(string invalidType)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this._sut.SendTestAlertAsync("user1", invalidType, 1));

        this._trackingProxy.Verify(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsyncNestThrowsNotSupportedException()
    {
        // Nest alarms are a valid UI type but PoracleNG's /api/test endpoint has no nest
        // surface — the builder throws NotSupportedException which the controller maps to
        // HTTP 501. Regression guard against reviving the best-effort nest payload.
        var alarms = CreateJsonArray(new
        {
            uid = 10,
            pokemon_id = 246
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync("nest", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TestUser",
            type = "discord:user"
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            this._sut.SendTestAlertAsync("user1", "nest", 10));

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsyncAlarmNotFoundThrowsKeyNotFoundException()
    {
        var alarms = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 25
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TestUser",
            type = "discord:user"
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            this._sut.SendTestAlertAsync("user1", "pokemon", 999));

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsyncUserNotFoundThrowsInvalidOperationException()
    {
        var alarms = CreateJsonArray(new
        {
            uid = 1,
            pokemon_id = 25
        });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync((JsonElement?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this._sut.SendTestAlertAsync("user1", "pokemon", 1));

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsyncEmptyAlarmListThrowsKeyNotFoundException()
    {
        var alarms = CreateJsonArray();
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new
        {
            id = "user1",
            name = "TestUser",
            type = "discord:user"
        });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            this._sut.SendTestAlertAsync("user1", "pokemon", 42));
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    private static JsonElement CreateJsonElement(object item)
    {
        var jsonStr = JsonSerializer.Serialize(item, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
