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

/// <summary>
/// PoracleNG renders a test alert through the member's own DTS template, so the alert itself cannot be
/// labelled. After PoracleNG accepts <c>/api/test</c> the service posts a plain note to the same target
/// through <c>deliverMessages</c>, so the DM cannot pass for a live alert (#1097). The note is a courtesy:
/// it must never be sent for a refused test and must never fail one that went out.
/// </summary>
public class TestAlertNoteTests
{
    private const string WebhookUrl = "https://hooks.example.test/webhooks/1/secret-token";

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleApiProxy> _apiProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _trackingProxy = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IMasterDataService> _masterData = new();
    private readonly RecordingLogger _logger = new();
    private readonly TestAlertService _sut;

    public TestAlertNoteTests()
    {
        var builders = new ITestPayloadBuilder[]
        {
            new PokemonTestPayloadBuilder(new PvpRankService(new MemoryCache(new MemoryCacheOptions())), this._masterData.Object),
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
            Helpers.LinkedTestKit.Unlinked(),
            this._logger);
    }

    [Theory]
    [InlineData("user1", "discord:user")]
    [InlineData("tg-user-9", "telegram:user")]
    [InlineData(WebhookUrl, "webhook")]
    [InlineData("chan-1", "discord:channel")]
    [InlineData("-1001234567890", "telegram:group")]
    [InlineData("-1001234567890:7", "telegram:topic")]
    public async Task AcceptedTestSendsTheNoteToTheSameTargetAndType(string id, string type)
    {
        this.GivenAnAlarmFor(id, type);

        await this._sut.SendTestAlertAsync(id, "pokemon", 42);

        this._apiProxy.Verify(p => p.DeliverTextAsync(id, type, TestAlertService.TestNote), Times.Once);
        this._apiProxy.Verify(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task TheNoteGoesOutAfterPoracleNgAcceptsTheTest()
    {
        this.GivenAnAlarmFor("user1", "discord:user");
        var order = new List<string>();
        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()))
            .Callback(() => order.Add("test"))
            .Returns(Task.CompletedTask);
        this._apiProxy.Setup(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => order.Add("note"))
            .Returns(Task.CompletedTask);

        await this._sut.SendTestAlertAsync("user1", "pokemon", 42);

        Assert.Equal(["test", "note"], order);
    }

    [Fact]
    public async Task ARefusedTestSendsNoNote()
    {
        this.GivenAnAlarmFor("user1", "discord:user");
        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()))
            .ThrowsAsync(new PoracleRequestRefusedException("message delivery not configured", 502));

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.SendTestAlertAsync("user1", "pokemon", 42));

        this._apiProxy.Verify(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreachablePoracleNgSendsNoNote()
    {
        this.GivenAnAlarmFor("user1", "discord:user");
        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.SendTestAlertAsync("user1", "pokemon", 42));

        this._apiProxy.Verify(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AFailedNoteDoesNotFailTheTest()
    {
        this.GivenAnAlarmFor("user1", "discord:user");
        this._apiProxy.Setup(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("503"));

        await this._sut.SendTestAlertAsync("user1", "pokemon", 42);

        this._apiProxy.Verify(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>()), Times.Once);
        Assert.Contains(this._logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AFailedNoteDoesNotLogTheWebhookUrl()
    {
        this.GivenAnAlarmFor(WebhookUrl, "webhook");
        this._apiProxy.Setup(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("503"));

        await this._sut.SendTestAlertAsync(WebhookUrl, "pokemon", 42);

        var warning = Assert.Single(this._logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain("secret-token", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.example.test", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNoteIsSentOnceWhateverTheAlarmType()
    {
        this._trackingProxy.Setup(p => p.GetByUserAsync("gym", "user1")).ReturnsAsync(Json(new[] { new { uid = 7, team = 1 } }));
        this._humanProxy.Setup(p => p.GetHumanAsync("user1")).ReturnsAsync(Json(new { id = "user1", name = "n", type = "discord:user" }));

        await this._sut.SendTestAlertAsync("user1", "gym", 7);

        this._apiProxy.Verify(p => p.DeliverTextAsync("user1", "discord:user", TestAlertService.TestNote), Times.Once);
    }

    [Fact]
    public void TheNoteSaysItIsATestAndDoesNotPointAtWhereTheAlertSits()
    {
        // Ordering through PoracleNG's render queue is not guaranteed, so "above" and "below" can be wrong.
        var note = TestAlertService.TestNote;

        Assert.Contains("Test notification", note, StringComparison.Ordinal);
        Assert.DoesNotContain("above", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("below", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheNoteSurvivesTelegramsDefaultMarkdownParse()
    {
        // PoracleNG sends Telegram text with parse_mode Markdown unless the message names another, and the
        // legacy Markdown parser rejects a message with an unbalanced entity character.
        Assert.DoesNotContain(TestAlertService.TestNote, c => c is '_' or '*' or '`' or '[' or ']');
        Assert.All(TestAlertService.TestNote, c => Assert.True(c < 128));
    }

    private void GivenAnAlarmFor(string id, string type)
    {
        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", id))
            .ReturnsAsync(Json(new[] { new { uid = 42, pokemon_id = 25, form = 0, template = "default" } }));
        this._humanProxy.Setup(p => p.GetHumanAsync(id))
            .ReturnsAsync(Json(new { id, name = "Test", type, language = "en", latitude = 40.7128, longitude = -74.006 }));
        this._apiProxy.Setup(p => p.SendTestAlertAsync(It.IsAny<TestAlertRequest>())).Returns(Task.CompletedTask);
        this._apiProxy.Setup(p => p.DeliverTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
    }

    private static JsonElement Json(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, SnakeCaseOptions));
        return doc.RootElement.Clone();
    }

    private sealed record Entry(LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger<TestAlertService>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            this.Entries.Add(new Entry(logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
        }
    }
}
