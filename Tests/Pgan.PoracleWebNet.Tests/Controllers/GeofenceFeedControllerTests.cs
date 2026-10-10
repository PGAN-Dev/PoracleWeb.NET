using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class GeofenceFeedControllerTests
{
    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<IKojiService> _kojiService = new();
    private readonly Mock<ILogger<GeofenceFeedController>> _logger = new();
    private readonly Mock<ISiteSettingService> _siteSettings = new();
    private const string Secret = "shared-secret";

    private readonly GeofenceFeedController _sut;

    public GeofenceFeedControllerTests()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync([]);
        this._siteSettings.Setup(x => x.GetByKeyAsync(HiddenAreas.SettingKey)).ReturnsAsync((SiteSetting?)null);
        this._sut = Build(Secret);
    }

    /// <summary>A controller wired to one configured secret, with a request whose header can be set.</summary>
    private GeofenceFeedController Build(string? configuredSecret, string? suppliedHeader = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Poracle:ApiSecret"] = configuredSecret })
            .Build();

        var controller = new GeofenceFeedController(
            this._repository.Object, this._kojiService.Object, this._siteSettings.Object, configuration, this._logger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        if (suppliedHeader is not null)
        {
            controller.Request.Headers["X-Poracle-Secret"] = suppliedHeader;
        }

        return controller;
    }

    // ──────────────────────────────────────────────────────────────
    // Dropping the Koji cache (#844)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshDropsTheKojiCacheWhenTheSecretMatches()
    {
        var sut = this.Build(Secret, Secret);

        Assert.IsType<OkObjectResult>(sut.RefreshKojiCache());
        this._kojiService.Verify(k => k.InvalidateAdminGeofenceCache(), Times.Once);
    }

    /// <remarks>
    /// A trailing-space variant used to be asserted here and was wrong. Kestrel strips optional trailing
    /// whitespace from a header value per RFC 9110 5.5, so "shared-secret " arrives already trimmed and
    /// is accepted over real HTTP. The assertion described a server that does not exist, and a directly
    /// constructed controller could never have noticed. See GeofenceFeedPipelineTests.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-secret")]
    [InlineData("SHARED-SECRET")]
    public void RefreshRefusesAnythingButTheConfiguredSecret(string? supplied)
    {
        var sut = this.Build(Secret, supplied);

        Assert.IsType<UnauthorizedResult>(sut.RefreshKojiCache());
        this._kojiService.Verify(k => k.InvalidateAdminGeofenceCache(), Times.Never);
    }

    /// <summary>
    /// With no secret configured there is nothing to check against, so the endpoint has to refuse rather
    /// than wave everyone through. An empty configured secret matching an empty header would otherwise
    /// make this the one anonymous write on the site.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("", "anything")]
    public void RefreshFailsClosedWhenNoSecretIsConfigured(string? configured, string? supplied)
    {
        var sut = this.Build(configured, supplied);

        Assert.IsType<UnauthorizedResult>(sut.RefreshKojiCache());
        this._kojiService.Verify(k => k.InvalidateAdminGeofenceCache(), Times.Never);
    }

    /// <summary>The feed itself stays open; only the refresh is gated.</summary>
    [Fact]
    public async Task TheFeedIsStillReadableWithoutASecret()
    {
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);

        Assert.IsType<OkObjectResult>(await this.Build(Secret).GetPoracleFeed());
    }

    [Fact]
    public async Task GetPoracleFeedReturnsOkWithDataArray()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofences = new List<UserGeofence>
        {
            new()
            {
                Id = 1,
                KojiName = "downtown",
                PolygonJson = JsonSerializer.Serialize(polygon),
                Status = "active"
            }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetPoracleFeedReturnsEmptyDataWhenNoGeofences()
    {
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetPoracleFeedFiltersOutGeofencesWithNullPolygonJson()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "no_polygon", PolygonJson = null, Status = "active" },
            new() { Id = 2, KojiName = "empty_polygon", PolygonJson = "", Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetPoracleFeedFiltersOutGeofencesWithInvalidPolygonJson()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "bad_json", PolygonJson = "not valid json", Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetPoracleFeedFiltersOutPolygonsWithFewerThan3Points()
    {
        var twoPoints = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0] };
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "too_few", PolygonJson = JsonSerializer.Serialize(twoPoints), Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetPoracleFeedUserGeofencesHaveCorrectFormat()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofences = new List<UserGeofence>
        {
            new()
            {
                Id = 42,
                KojiName = "my_area",
                PolygonJson = JsonSerializer.Serialize(polygon),
                Status = "active"
            }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());

        var item = data[0];
        Assert.Equal("my_area", item.GetProperty("name").GetString());
        Assert.False(item.GetProperty("userSelectable").GetBoolean());
        Assert.False(item.GetProperty("displayInMatches").GetBoolean());
        Assert.Equal(3, item.GetProperty("path").GetArrayLength());
    }

    [Fact]
    public async Task GetPoracleFeedIncludesMultipleValidGeofences()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var polygonJson = JsonSerializer.Serialize(polygon);
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", PolygonJson = polygonJson, Status = "active" },
            new() { Id = 2, KojiName = "area2", PolygonJson = polygonJson, Status = "pending_review" },
            new() { Id = 3, KojiName = "no_polygon", PolygonJson = null, Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task GetPoracleFeedCombinesAdminAndUserGeofences()
    {
        var adminGeofences = new List<AdminGeofence>
        {
            new()
            {
                Id = 1,
                Name = "Aberdeen",
                Group = "US - VA - Hampton Roads North",
                Path = [[37.0, -76.0], [37.1, -76.1], [37.2, -76.2]],
                UserSelectable = true,
                DisplayInMatches = true,
                Description = "",
                Color = "#3399ff"
            }
        };
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync(adminGeofences);

        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var userGeofences = new List<UserGeofence>
        {
            new() { Id = 100, KojiName = "user_area", PolygonJson = JsonSerializer.Serialize(polygon), Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(userGeofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        // Should contain both admin and user geofences
        Assert.Equal(2, data.GetArrayLength());

        // First item is admin (userSelectable: true, displayInMatches: true)
        var admin = data[0];
        Assert.Equal("Aberdeen", admin.GetProperty("name").GetString());
        Assert.Equal("US - VA - Hampton Roads North", admin.GetProperty("group").GetString());
        Assert.True(admin.GetProperty("userSelectable").GetBoolean());
        Assert.True(admin.GetProperty("displayInMatches").GetBoolean());

        // Second item is user (userSelectable: false, displayInMatches: false)
        var user = data[1];
        Assert.Equal("user_area", user.GetProperty("name").GetString());
        Assert.False(user.GetProperty("userSelectable").GetBoolean());
        Assert.False(user.GetProperty("displayInMatches").GetBoolean());
    }

    [Fact]
    public async Task GetPoracleFeedStillServesUserGeofencesWhenKojiFails()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ThrowsAsync(new HttpRequestException("Koji unreachable"));

        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var userGeofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "my_area", PolygonJson = JsonSerializer.Serialize(polygon), Status = "active" }
        };
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(userGeofences);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task GetPoracleFeedAdminGeofencesIncludeColorAndDescription()
    {
        var adminGeofences = new List<AdminGeofence>
        {
            new()
            {
                Id = 1,
                Name = "TestArea",
                Group = "TestGroup",
                Path = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]],
                UserSelectable = true,
                DisplayInMatches = true,
                Description = "A test area",
                Color = "#ff0000"
            }
        };
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync(adminGeofences);
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);

        var result = await this._sut.GetPoracleFeed();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("data")[0];
        Assert.Equal("A test area", item.GetProperty("description").GetString());
        Assert.Equal("#ff0000", item.GetProperty("color").GetString());
    }

    // ── Malformed polygons must not reach the shared feed (#410) ────────────────
    // This endpoint is anonymous and is the single geofence source for PoracleJS, so one user's bad
    // polygon is every user's problem.

    private static List<UserGeofence> FeedRows(params double[][][] polygons)
    {
        var rows = new List<UserGeofence>();
        for (var i = 0; i < polygons.Length; i++)
        {
            rows.Add(new UserGeofence
            {
                Id = i + 1,
                KojiName = $"fence{i}",
                PolygonJson = JsonSerializer.Serialize(polygons[i])
            });
        }

        return rows;
    }

    private async Task<int> FeedCountAsync()
    {
        var result = await this._sut.GetPoracleFeed();
        var json = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        return JsonDocument.Parse(json).RootElement.GetProperty("data").GetArrayLength();
    }

    [Fact]
    public async Task FeedSkipsPolygonsWhosePointsAreNotPairs()
    {
        double[][] good = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]];
        double[][] bad = [[1.0], [2.0], [3.0]];
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(FeedRows(good, bad));

        Assert.Equal(1, await this.FeedCountAsync());
    }

    [Fact]
    public async Task FeedSkipsPolygonsWithCoordinatesOffTheGlobe()
    {
        double[][] bad = [[999, -999], [998, -998], [997, -997]];
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(FeedRows(bad));

        Assert.Equal(0, await this.FeedCountAsync());
    }

    [Fact]
    public async Task FeedStillServesWellFormedPolygons()
    {
        double[][] good = [[40.0, -75.0], [40.01, -75.0], [40.01, -74.99]];
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(FeedRows(good, good));

        Assert.Equal(2, await this.FeedCountAsync());
    }
    // ──────────────────────────────────────────────────────────────
    // Degrading when a source is down
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFeedStillServesUserFencesWhenKojiIsDown()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ThrowsAsync(new HttpRequestException("koji down"));
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);

        Assert.IsType<OkObjectResult>(await this.Build(Secret).GetPoracleFeed());
    }

    /// <summary>
    /// The half that did not degrade. Koji's read has always been wrapped; the database read was not,
    /// so a blip answered 500 and took down the only geofence source PoracleJS has.
    /// </summary>
    [Fact]
    public async Task TheFeedStillServesKojiFencesWhenTheDatabaseIsDown()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync([]);
        this._repository.Setup(r => r.GetAllActiveAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        Assert.IsType<OkObjectResult>(await this.Build(Secret).GetPoracleFeed());
    }

    /// <summary>
    /// With both sources down the feed must NOT answer 200 and an empty list. PoracleJS treats this feed
    /// as authoritative and caches the last good response, so an empty success is not a degraded answer,
    /// it is an instruction to drop every geofence every user has. An error leaves the cache in place.
    /// </summary>
    [Fact]
    public async Task TheFeedRefusesRatherThanServingAnEmptyListWhenBothSourcesAreDown()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ThrowsAsync(new HttpRequestException("koji down"));
        this._repository.Setup(r => r.GetAllActiveAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        var result = Assert.IsType<ObjectResult>(await this.Build(Secret).GetPoracleFeed());

        Assert.Equal(503, result.StatusCode);
    }

    /// <summary>
    /// The legitimate-case half, and the reason the check is on failure rather than on emptiness: an
    /// instance with no Koji project and no user-drawn fences has an empty feed, and that is a correct
    /// answer rather than an outage.
    /// </summary>
    [Fact]
    public async Task AnEmptyFeedIsStillASuccessWhenBothSourcesAnswered()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync([]);
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);

        Assert.IsType<OkObjectResult>(await this.Build(Secret).GetPoracleFeed());
    }

    // ──────────────────────────────────────────────────────────────
    // Hiding an area from the pickers (#885)
    // ──────────────────────────────────────────────────────────────

    /// <summary>Two admin fences, one of which an operator may want off the menu.</summary>
    private void GivenKojiServes(params string[] names)
    {
        // The feed merges user geofences in too; these tests are about the admin half.
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync(
            names.Select((n, i) => new AdminGeofence
            {
                Id = i + 1,
                Name = n,
                Group = "test",
                Path = [[0, 0], [0, 1], [1, 1], [0, 0]],
                UserSelectable = true,
                DisplayInMatches = true,
            }).ToList());
    }

    private void GivenHidden(string? rawSettingValue) =>
        this._siteSettings.Setup(x => x.GetByKeyAsync(HiddenAreas.SettingKey))
            .ReturnsAsync(rawSettingValue is null ? null : new SiteSetting { Key = HiddenAreas.SettingKey, Value = rawSettingValue });

    /// <summary>The feed as JSON, whichever result type the action used to return it.</summary>
    private static JsonElement[] FeedOf(IActionResult result)
    {
        var body = Assert.IsType<OkObjectResult>(result).Value!;
        var envelope = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(body));
        return envelope.GetProperty("data").Deserialize<JsonElement[]>()!;
    }

    private static bool SelectableOf(JsonElement[] feed, string name) =>
        feed.Single(f => f.GetProperty("name").GetString() == name).GetProperty("userSelectable").GetBoolean();

    [Fact]
    public async Task AHiddenAreaIsServedAsNotSelectable()
    {
        this.GivenKojiServes("staging", "downtown");
        this.GivenHidden("[\"staging\"]");

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.False(SelectableOf(feed, "staging"));
        Assert.True(SelectableOf(feed, "downtown"));
    }

    /// <summary>
    /// The fence stays in the feed. Dropping it would stop it matching for anyone already subscribed
    /// and would break the per-alarm scopes that reference it by name.
    /// </summary>
    [Fact]
    public async Task AHiddenAreaIsStillServed()
    {
        this.GivenKojiServes("staging", "downtown");
        this.GivenHidden("[\"staging\"]");

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.Equal(2, feed.Length);
        Assert.Contains(feed, f => f.GetProperty("name").GetString() == "staging");
    }

    /// <summary>
    /// displayInMatches is deliberately untouched. Someone already subscribed keeps matching a hidden
    /// fence, and blanking its name out of their alert would make that harder to diagnose, not easier.
    /// </summary>
    [Fact]
    public async Task HidingAnAreaDoesNotChangeWhetherItsNameShowsInAlerts()
    {
        this.GivenKojiServes("staging");
        this.GivenHidden("[\"staging\"]");

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.True(feed.Single().GetProperty("displayInMatches").GetBoolean());
    }

    [Fact]
    public async Task HidingMatchesRegardlessOfCase()
    {
        this.GivenKojiServes("Downtown - Richmond");
        this.GivenHidden("[\"downtown - richmond\"]");

        Assert.False(SelectableOf(FeedOf(await this._sut.GetPoracleFeed()), "Downtown - Richmond"));
    }

    [Fact]
    public async Task NothingIsHiddenWhenNothingIsStored()
    {
        this.GivenKojiServes("staging", "downtown");
        this.GivenHidden(null);

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.True(SelectableOf(feed, "staging"));
        Assert.True(SelectableOf(feed, "downtown"));
    }

    /// <summary>
    /// The direction that matters. This runs inside the single geofence source for Poracle, so an
    /// unreadable row must leave every area selectable rather than hide the lot — the second failure
    /// is silent and takes alerting with it.
    /// </summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"not\":\"an array\"}")]
    public async Task AnUnreadableSettingHidesNothing(string raw)
    {
        this.GivenKojiServes("staging", "downtown");
        this.GivenHidden(raw);

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.True(SelectableOf(feed, "staging"));
        Assert.True(SelectableOf(feed, "downtown"));
    }

    [Fact]
    public async Task ASettingsFailureHidesNothingRatherThanFailingTheFeed()
    {
        this.GivenKojiServes("staging");
        this._siteSettings.Setup(x => x.GetByKeyAsync(HiddenAreas.SettingKey)).ThrowsAsync(new InvalidOperationException("db down"));

        var feed = FeedOf(await this._sut.GetPoracleFeed());

        Assert.True(SelectableOf(feed, "staging"));
    }

    /// <summary>
    /// A fence Koji already marks non-selectable stays non-selectable. Our list only ever removes
    /// selectability; it never grants it.
    /// </summary>
    [Fact]
    public async Task OurListNeverMakesAKojiPrivateFenceSelectable()
    {
        this._repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync([]);
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync(
        [
            new AdminGeofence { Id = 1, Name = "private", Group = "test", Path = [[0, 0], [0, 1], [1, 1], [0, 0]], UserSelectable = false, DisplayInMatches = true },
        ]);
        this.GivenHidden("[]");

        Assert.False(SelectableOf(FeedOf(await this._sut.GetPoracleFeed()), "private"));
    }
}
