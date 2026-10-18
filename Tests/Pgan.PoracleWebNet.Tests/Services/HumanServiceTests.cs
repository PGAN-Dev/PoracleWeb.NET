using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class HumanServiceTests
{
    private readonly Mock<IHumanRepository> _repository = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IPoracleTrackingProxy> _trackingProxy = new();
    private readonly HumanService _sut;

    public HumanServiceTests() => this._sut = new HumanService(
        this._repository.Object,
        this._humanProxy.Object,
        this._trackingProxy.Object);

    [Fact]
    public async Task GetAllAsyncReturnsHumansFromRepository()
    {
        // GetAllAsync still uses direct DB (no proxy equivalent)
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(
        [
            new() { Id = "u1", Name = "User1" },
            new() { Id = "u2", Name = "User2" }
        ]);

        var result = (await this._sut.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsHumanFromProxy()
    {
        var json = CreateHumanJson("u1", "User1");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.GetByIdAsync("u1");
        Assert.NotNull(result);
        Assert.Equal("u1", result!.Id);
        Assert.Equal("User1", result.Name);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullWhenProxyReturnsNull()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("unknown")).ReturnsAsync((JsonElement?)null);
        Assert.Null(await this._sut.GetByIdAsync("unknown"));
    }

    [Fact]
    public async Task GetByIdAsyncThrowsOnProxyError()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.GetByIdAsync("u1"));
    }


    [Fact]
    public async Task CreateAsyncUsesProxy()
    {
        var human = new Human { Id = "u1", Name = "New", Type = "discord:user" };
        this._humanProxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>())).Returns(Task.CompletedTask);

        // After create, re-fetch returns the human
        var json = CreateHumanJson("u1", "New");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);

        var result = await this._sut.CreateAsync(human);
        Assert.Equal("New", result.Name);
        this._humanProxy.Verify(p => p.CreateHumanAsync(It.IsAny<JsonElement>()), Times.Once);
    }

    /// <summary>
    /// PoracleNG's <c>createHumanRequest</c> declares <c>Enabled</c> and <c>AdminDisable</c> as <c>*bool</c>.
    /// These are stored as <c>int</c> here, and sending the raw int failed with
    /// "json: cannot unmarshal number into Go struct field createHumanRequest.enabled of type bool",
    /// so admin webhook creation could never succeed. The old test only asserted that the proxy was called
    /// with any JsonElement, which is why the drift went unnoticed.
    /// </summary>
    [Theory]
    [InlineData(1, 0, true, false)]
    [InlineData(0, 1, false, true)]
    public async Task CreateAsyncSendsEnabledAndAdminDisableAsBooleans(
        int enabled, int adminDisable, bool expectedEnabled, bool expectedAdminDisable)
    {
        var human = new Human { Id = "u1", Name = "Hook", Type = "webhook", Enabled = enabled, AdminDisable = adminDisable };

        JsonElement sent = default;
        this._humanProxy.Setup(p => p.CreateHumanAsync(It.IsAny<JsonElement>()))
            .Callback<JsonElement>(b => sent = b.Clone())
            .Returns(Task.CompletedTask);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(CreateHumanJson("u1", "Hook"));

        await this._sut.CreateAsync(human);

        // JSON booleans, not numbers — a number is what PoracleNG rejected.
        Assert.NotEqual(JsonValueKind.Number, sent.GetProperty("enabled").ValueKind);
        Assert.NotEqual(JsonValueKind.Number, sent.GetProperty("admin_disable").ValueKind);
        Assert.Equal(expectedEnabled, sent.GetProperty("enabled").GetBoolean());
        Assert.Equal(expectedAdminDisable, sent.GetProperty("admin_disable").GetBoolean());
    }

    [Fact]
    public async Task SetLanguageAsyncGoesThroughTheProxyAndNotTheDatabase()
    {
        // The repository has no write left to delegate to. Before this, changing the notification
        // language read the whole human, mutated one field and wrote every column back -- which is how
        // it managed to stamp last_checked as well (#517) -- and PoracleNG never learned about it until
        // the next restart, because nothing reloaded its state.
        await this._sut.SetLanguageAsync("u1", "de");

        this._humanProxy.Verify(p => p.SetLanguageAsync("u1", "de"), Times.Once);
        this._repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueViaProxy()
    {
        var json = CreateHumanJson("u1", "User1");
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(json);
        Assert.True(await this._sut.ExistsAsync("u1"));
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseViaProxy()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("unknown")).ReturnsAsync((JsonElement?)null);
        Assert.False(await this._sut.ExistsAsync("unknown"));
    }

    [Fact]
    public async Task ExistsAsyncThrowsOnProxyError()
    {
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.ExistsAsync("u1"));
    }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsyncUsesTrackingProxy()
    {
        // Set up tracking responses with UIDs for each alarm type
        var pokemonJson = CreateTrackingArray([1, 2]);
        var raidJson = CreateTrackingArray([3]);
        var emptyJson = CreateTrackingArray([]);

        this._trackingProxy.Setup(p => p.GetByUserAsync("pokemon", "u1")).ReturnsAsync(pokemonJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("raid", "u1")).ReturnsAsync(raidJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("egg", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("quest", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("nest", "u1")).ReturnsAsync(emptyJson);
        this._trackingProxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(emptyJson);

        var count = await this._sut.DeleteAllAlarmsByUserAsync("u1");

        Assert.Equal(3, count);
        this._trackingProxy.Verify(p => p.BulkDeleteByUidsAsync("pokemon", "u1", It.Is<IEnumerable<int>>(uids => uids.Count() == 2)), Times.Once);
        this._trackingProxy.Verify(p => p.BulkDeleteByUidsAsync("raid", "u1", It.Is<IEnumerable<int>>(uids => uids.Count() == 1)), Times.Once);
    }

    [Fact]
    public async Task DeleteAllAlarmsByUserAsyncThrowsOnProxyError()
    {
        this._trackingProxy.Setup(p => p.GetByUserAsync(It.IsAny<string>(), "u1"))
            .ThrowsAsync(new HttpRequestException("fail"));
        await Assert.ThrowsAsync<HttpRequestException>(() => this._sut.DeleteAllAlarmsByUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncDelegatesToRepository()
    {
        this._repository.Setup(r => r.DeleteUserAsync("u1")).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteUserAsync("u1"));
    }

    [Fact]
    public async Task DeleteUserAsyncReturnsFalse()
    {
        this._repository.Setup(r => r.DeleteUserAsync("unknown")).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteUserAsync("unknown"));
    }

    /// <summary>
    /// Creates a JsonElement representing a human record from the PoracleNG API (snake_case).
    /// </summary>
    private static JsonElement CreateHumanJson(string id, string name, int enabled = 1, int adminDisable = 0, int currentProfileNo = 1)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            name,
            type = "discord:user",
            enabled,
            admin_disable = adminDisable,
            current_profile_no = currentProfileNo,
            area = "[]",
            latitude = 0.0,
            longitude = 0.0,
            fails = 0,
            language = "en",
            community_membership = (string?)null,
        });

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Creates a JsonElement representing a tracking array response with the given UIDs.
    /// </summary>
    private static JsonElement CreateTrackingArray(int[] uids)
    {
        var items = uids.Select(uid => new { uid }).ToArray();
        var json = JsonSerializer.Serialize(items);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// <c>humans</c> is keyed on <c>id</c> alone, so a lookup that also filtered on
    /// <c>current_profile_no</c> could only ever return a spurious null when the JWT's profile claim was
    /// stale — which PoracleNG's active-hours scheduler and the bot's <c>!profile</c> command both cause
    /// routinely. That method produced three separate user-visible bugs (a raw Discord ID on the geofence
    /// review card, a 404 on the language endpoints, and a silent no-op in the geofence approval area swap)
    /// and has been removed. This test fails the build if it is reintroduced.
    /// </summary>
    [Fact]
    public void NoProfileFilteredHumanLookupExists()
    {
        var offenders = new[] { typeof(IHumanService), typeof(IHumanRepository) }
            .SelectMany(t => t.GetMethods())
            .Where(m => m.Name.Contains("AndProfile", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A human lookup filtered by profile number is back: " + string.Join(", ", offenders)
            + ". humans has one row per id — use GetByIdAsync.");
    }

    // The same human, captured live from dev-01: first from a released PoracleNG (5.2.1), then from a build
    // carrying jfberry/PoracleNG#303, which sends flags as booleans and area lists as arrays.
    private const string ReleasedHumanJson =
        """{"id":"114774755437051904","type":"discord:user","name":"x","enabled":1,"area":"[\"downtown\",\"west end\"]","latitude":41.6,"longitude":-83.5,"fails":0,"last_checked":"2026-10-17T12:00:00Z","language":"en","admin_disable":0,"disabled_date":null,"current_profile_no":1,"community_membership":"[\"avoca\"]","area_restriction":null,"notes":"","blocked_alerts":null}""";

    private const string V2OwnedHumanJson =
        """{"id":"114774755437051904","type":"discord:user","name":"x","enabled":true,"admin_disable":false,"area":["downtown","west end"],"latitude":41.6,"longitude":-83.5,"language":"en","current_profile_no":1,"community_membership":["avoca"],"area_restriction":null,"blocked_alerts":[],"fails":0,"last_checked":"2026-10-17T12:00:00Z","disabled_date":null,"notes":""}""";

    private async Task<Human> ReadHumanAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        this._humanProxy.Setup(p => p.GetHumanAsync("114774755437051904")).ReturnsAsync(doc.RootElement.Clone());

        var human = await this._sut.GetByIdAsync("114774755437051904");
        Assert.NotNull(human);
        return human!;
    }

    [Fact]
    public async Task TheReleasedShapeStillReads()
    {
        var human = await this.ReadHumanAsync(ReleasedHumanJson);

        Assert.Equal(1, human.Enabled);
        Assert.Equal(0, human.AdminDisable);
        Assert.Equal("[\"downtown\",\"west end\"]", human.Area);
        Assert.Equal("[\"avoca\"]", human.CommunityMembership);
    }

    [Fact]
    public async Task TheV2OwnedShapeReads()
    {
        var human = await this.ReadHumanAsync(V2OwnedHumanJson);

        Assert.Equal(1, human.Enabled);
        Assert.Equal(0, human.AdminDisable);
        Assert.Equal("[\"downtown\",\"west end\"]", human.Area);
        Assert.Equal("[\"avoca\"]", human.CommunityMembership);
    }

    [Fact]
    public async Task ABooleanEnabledHumanIsEnabled()
    {
        var human = await this.ReadHumanAsync("""{"id":"114774755437051904","enabled":true}""");
        Assert.Equal(1, human.Enabled);
    }

    [Fact]
    public async Task ABooleanAdminDisabledHumanIsDisabled()
    {
        var human = await this.ReadHumanAsync("""{"id":"114774755437051904","enabled":true,"admin_disable":true}""");
        Assert.Equal(1, human.AdminDisable);
    }

    [Fact]
    public async Task AnEmptyAreaArrayIsNoAreas()
    {
        var human = await this.ReadHumanAsync("""{"id":"114774755437051904","area":[],"community_membership":[]}""");

        Assert.Equal("[]", human.Area);
        Assert.Equal("[]", human.CommunityMembership);
    }

}
