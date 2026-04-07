using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

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
    private readonly Mock<ILogger<TestAlertService>> _logger = new();
    private readonly TestAlertService _sut;

    public TestAlertServiceTests()
    {
        this._sut = new TestAlertService(
            this._apiProxy.Object,
            this._trackingProxy.Object,
            this._humanProxy.Object,
            this._logger.Object);
    }

    [Fact]
    public async Task SendTestAlertAsync_ValidPokemon_SendsRequest()
    {
        var alarms = CreateJsonArray(new { uid = 42, pokemon_id = 25, form = 0, template = "default" });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new { id = "user1", name = "TestUser", type = "discord:user", language = "en", latitude = 40.7128, longitude = -74.006 });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", "pokemon", 42);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Type == "pokemon" &&
            r.Target.Id == "user1" &&
            r.Target.Name == "TestUser" &&
            r.Target.Type == "discord" &&
            r.Target.Language == "en"
        )), Times.Once);
    }

    [Fact]
    public async Task SendTestAlertAsync_TelegramUser_SetsTargetTypeTelegram()
    {
        var alarms = CreateJsonArray(new { uid = 1, pokemon_id = 25 });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new { id = "user1", name = "TelegramUser", type = "telegram:user", language = "en", latitude = 0.0, longitude = 0.0 });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", "pokemon", 1);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Target.Type == "telegram"
        )), Times.Once);
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("egg")]
    [InlineData("quest")]
    [InlineData("invasion")]
    [InlineData("lure")]
    [InlineData("nest")]
    [InlineData("gym")]
    public async Task SendTestAlertAsync_AllValidTypes_SendsRequest(string alarmType)
    {
        var alarms = CreateJsonArray(new { uid = 10, pokemon_id = 150 });
        this._trackingProxy.Setup(p => p.GetByUserAsync(alarmType, "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new { id = "user1", name = "TestUser", type = "discord:user" });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", alarmType, 10);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.Is<TestAlertRequest>(r =>
            r.Type == alarmType
        )), Times.Once);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task SendTestAlertAsync_InvalidType_ThrowsArgumentException(string invalidType)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            this._sut.SendTestAlertAsync("user1", invalidType, 1));

        this._trackingProxy.Verify(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsync_AlarmNotFound_ThrowsKeyNotFoundException()
    {
        var alarms = CreateJsonArray(new { uid = 1, pokemon_id = 25 });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new { id = "user1", name = "TestUser", type = "discord:user" });
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(human);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            this._sut.SendTestAlertAsync("user1", "pokemon", 999));

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsync_UserNotFound_ThrowsInvalidOperationException()
    {
        var alarms = CreateJsonArray(new { uid = 1, pokemon_id = 25 });
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync((JsonElement?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this._sut.SendTestAlertAsync("user1", "pokemon", 1));

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAsync_EmptyAlarmList_ThrowsKeyNotFoundException()
    {
        var alarms = CreateJsonArray();
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "user1")).ReturnsAsync(alarms);

        var human = CreateJsonElement(new { id = "user1", name = "TestUser", type = "discord:user" });
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
