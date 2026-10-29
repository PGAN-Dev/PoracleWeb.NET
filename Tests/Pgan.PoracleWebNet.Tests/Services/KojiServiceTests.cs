using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class KojiServiceTests
{
    private const string ApiAddress = "http://localhost:8080";

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koji:ApiAddress"] = ApiAddress,
            ["Koji:ProjectId"] = "5",
            ["Koji:ProjectName"] = "PoracleJS"
        })
        .Build();

    private static KojiService CreateSut(CapturingHandler handler) =>
        new(new HttpClient(handler), CreateConfig(), new MemoryCache(new MemoryCacheOptions()), NullLogger<KojiService>.Instance);

    private static readonly double[][] s_polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]];

    // Koji resolves __parent as a geofence id. parentId 0 (a region-less geofence, issue #314) must be
    // serialized as JSON null, otherwise Koji returns HTTP 500 "[GEOFENCE]: Does not exist".
    [Fact]
    public async Task SaveGeofenceAsyncSendsNullParentWhenParentIdIsZero()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", string.Empty, parentId: 0, polygon: s_polygon, isPublic: true);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Null, parent.ValueKind);
    }

    [Fact]
    public async Task SaveGeofenceAsyncSendsNullParentWhenParentIdNegative()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", string.Empty, parentId: -1, polygon: s_polygon);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Null, parent.ValueKind);
    }

    [Fact]
    public async Task SaveGeofenceAsyncSendsNumericParentWhenParentIdPositive()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", "City", parentId: 42, polygon: s_polygon, isPublic: true);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Number, parent.ValueKind);
        Assert.Equal(42, parent.GetInt32());
    }

    private static JsonElement GetParentProperty(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var properties = doc.RootElement
            .GetProperty("area")
            .GetProperty("features")[0]
            .GetProperty("properties");
        return properties.GetProperty("__parent").Clone();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json")
            };
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Koji's own visibility flags (#885)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Serves the three calls GetAdminGeofencesAsync makes, with a poracle export the test controls.
    /// </summary>
    private sealed class FeedHandler(string poracleJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = url.Contains("/geofence/poracle/", StringComparison.Ordinal)
                ? poracleJson
                : """{"data":[]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static KojiService CreateFeedSut(string poracleJson) =>
        new(new HttpClient(new FeedHandler(poracleJson)), CreateConfig(), new MemoryCache(new MemoryCacheOptions()), NullLogger<KojiService>.Instance);

    private const string TriangleJson = """[[1.0,2.0],[3.0,4.0],[5.0,6.0]]""";

    /// <summary>
    /// These flags used to be hardcoded true, so a fence an operator had already made private in Koji
    /// was served to every user as selectable — the same leak #544 closed for user-drawn geofences,
    /// one source along.
    /// </summary>
    [Fact]
    public async Task AdminGeofencesCarryKojiPrivateFlags()
    {
        var json = $$"""
        {"data":[
          {"name":"public area","userSelectable":true,"displayInMatches":true,"path":{{TriangleJson}}},
          {"name":"private area","userSelectable":false,"displayInMatches":false,"path":{{TriangleJson}}}
        ]}
        """;

        var areas = (await CreateFeedSut(json).GetAdminGeofencesAsync()).ToList();

        Assert.True(areas.Single(a => a.Name == "public area").UserSelectable);
        Assert.False(areas.Single(a => a.Name == "private area").UserSelectable);
        Assert.False(areas.Single(a => a.Name == "private area").DisplayInMatches);
    }

    /// <summary>
    /// Absent means public. Koji omits the flags on an ordinary fence, and defaulting them to false
    /// would empty every instance's area list at once.
    /// </summary>
    [Fact]
    public async Task AnAreaWithoutTheFlagsStaysSelectable()
    {
        var json = $$"""{"data":[{"name":"plain","path":{{TriangleJson}}}]}""";

        var area = (await CreateFeedSut(json).GetAdminGeofencesAsync()).Single();

        Assert.True(area.UserSelectable);
        Assert.True(area.DisplayInMatches);
    }

    // ──────────────────────────────────────────────────────────────
    // Multi-part areas (#1117)
    // ──────────────────────────────────────────────────────────────

    private const string FarTriangleJson = """[[10.0,20.0],[11.0,21.0],[12.0,20.0]]""";

    /// <summary>
    /// Koji exports a MultiPolygon as <c>"path": []</c> beside <c>multipath</c>. The empty path used to fail
    /// the three-point check, so the area vanished from the list, from the feed and from PoracleNG.
    /// </summary>
    [Fact]
    public async Task AMultiPartAreaReachesTheListWithEveryPart()
    {
        var json = $$"""{"data":[{"name":"split","path":[],"multipath":[{{TriangleJson}},{{FarTriangleJson}}]}]}""";

        var area = (await CreateFeedSut(json).GetAdminGeofencesAsync()).Single();

        Assert.Equal("split", area.Name);
        Assert.Empty(area.Path);
        Assert.Equal(2, area.Multipath!.Length);
        Assert.Equal([10.0, 20.0], area.Multipath[1][0]);
        // The bounding box is the pre-filter for every containment test, so it has to cover both parts.
        Assert.Equal(1.0, area.MinLat);
        Assert.Equal(12.0, area.MaxLat);
        Assert.Equal(2.0, area.MinLon);
        Assert.Equal(21.0, area.MaxLon);
    }

    [Fact]
    public async Task AnOrdinaryAreaKeepsItsPathAndHasNoParts()
    {
        var json = $$"""{"data":[{"name":"plain","path":{{TriangleJson}}}]}""";

        var area = (await CreateFeedSut(json).GetAdminGeofencesAsync()).Single();

        Assert.Equal(3, area.Path.Length);
        Assert.Null(area.Multipath);
        Assert.Equal(5.0, area.MaxLat);
    }

    /// <summary>
    /// PoracleNG matches a fence on its path and every part alike, so one that carries both keeps both, as
    /// parts. The feed must never send both keys: PoracleJS reads <c>path</c> first and ignores the rest.
    /// </summary>
    [Fact]
    public async Task AnAreaCarryingBothKeysKeepsThePathAsAPart()
    {
        var json = $$"""{"data":[{"name":"both","path":{{TriangleJson}},"multipath":[{{FarTriangleJson}}]}]}""";

        var area = (await CreateFeedSut(json).GetAdminGeofencesAsync()).Single();

        Assert.Empty(area.Path);
        Assert.Equal(2, area.Multipath!.Length);
    }

    /// <summary>A part too small to be a polygon is dropped; one usable part left is an ordinary area.</summary>
    [Fact]
    public async Task ADegeneratePartIsDroppedAndOnePartLeftIsAPlainPath()
    {
        var json = $$"""{"data":[{"name":"one left","path":[],"multipath":[[[1.0,1.0],[2.0,2.0]],{{TriangleJson}}]}]}""";

        var area = (await CreateFeedSut(json).GetAdminGeofencesAsync()).Single();

        Assert.Equal(3, area.Path.Length);
        Assert.Null(area.Multipath);
    }

    [Fact]
    public async Task AnAreaWithNoUsableRingIsStillSkipped()
    {
        var json = $$"""{"data":[{"name":"empty","path":[],"multipath":[[[1.0,1.0],[2.0,2.0]]]},{"name":"plain","path":{{TriangleJson}}}]}""";

        var areas = await CreateFeedSut(json).GetAdminGeofencesAsync();

        Assert.Equal("plain", Assert.Single(areas).Name);
    }

    /// <summary>
    /// Answers each Koji call from a function of its URL and keeps every request body, for the calls that
    /// read a feature and then save it back.
    /// </summary>
    private sealed class RoutingHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                this.Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request.RequestUri!.ToString()), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static KojiService CreateRoutingSut(RoutingHandler handler) =>
        new(new HttpClient(handler), CreateConfig(), new MemoryCache(new MemoryCacheOptions()), NullLogger<KojiService>.Instance);

    // GeoJSON order, [lon, lat].
    private const string SquareRingGeoJson = """[[0.0,0.0],[1.0,0.0],[1.0,1.0],[0.0,1.0],[0.0,0.0]]""";
    private const string FarSquareRingGeoJson = """[[20.0,10.0],[21.0,10.0],[21.0,11.0],[20.0,11.0],[20.0,10.0]]""";

    private static string FeatureJson(string displayName, string geometry) =>
        $$$"""{"data":{"type":"Feature","properties":{"name":"{{{displayName}}}"},"geometry":{{{geometry}}}}}""";

    /// <summary>
    /// A MultiPolygon region used to throw inside the parse after the array was allocated, and the bare catch
    /// returned the region with a polygon full of nulls, which the region selector then tripped over.
    /// </summary>
    [Fact]
    public async Task AMultiPolygonRegionKeepsEveryPart()
    {
        var multi = $$"""{"type":"MultiPolygon","coordinates":[[{{SquareRingGeoJson}}],[{{FarSquareRingGeoJson}}]]}""";
        var single = $$"""{"type":"Polygon","coordinates":[{{SquareRingGeoJson}}]}""";
        var handler = new RoutingHandler(url => url switch
        {
            _ when url.Contains("/geofence/reference", StringComparison.Ordinal) =>
                """{"data":[{"id":1,"name":"islands"},{"id":2,"name":"town","parent":1},{"id":3,"name":"mainland"},{"id":4,"name":"village","parent":3}]}""",
            _ when url.Contains("/area/islands", StringComparison.Ordinal) => FeatureJson("The Islands", multi),
            _ when url.Contains("/area/mainland", StringComparison.Ordinal) => FeatureJson("The Mainland", single),
            _ => """{"data":[]}""",
        });

        var regions = await CreateRoutingSut(handler).GetRegionsAsync();

        var islands = regions.Single(r => r.Name == "islands");
        Assert.Equal("The Islands", islands.DisplayName);
        Assert.Null(islands.Polygon);
        Assert.Equal(2, islands.Multipath!.Length);
        Assert.Equal([10.0, 20.0], islands.Multipath[1][0]);

        var mainland = regions.Single(r => r.Name == "mainland");
        Assert.Equal("The Mainland", mainland.DisplayName);
        Assert.Null(mainland.Multipath);
        Assert.Equal(5, mainland.Polygon!.Length);
        Assert.Equal([0.0, 1.0], mainland.Polygon[1]);
    }

    /// <summary>
    /// Removing an area from the project re-saves its geometry. It used to rebuild a Polygon from the first
    /// ring, which threw on a MultiPolygon and would have dropped a polygon's holes.
    /// </summary>
    [Fact]
    public async Task RemovingAMultiPartAreaSendsEveryPartBack()
    {
        var multi = $$"""{"type":"MultiPolygon","coordinates":[[{{SquareRingGeoJson}}],[{{FarSquareRingGeoJson}}]]}""";
        var handler = new RoutingHandler(url => url.Contains("/area/", StringComparison.Ordinal)
            ? FeatureJson("Split", multi)
            : """{"status":"ok"}""");

        await CreateRoutingSut(handler).RemoveGeofenceFromProjectAsync("split");

        var feature = SavedFeature(Assert.Single(handler.Bodies));
        var geometry = feature.GetProperty("geometry");
        Assert.Equal("MultiPolygon", geometry.GetProperty("type").GetString());
        Assert.Equal(2, geometry.GetProperty("coordinates").GetArrayLength());
        Assert.Equal(20.0, geometry.GetProperty("coordinates")[1][0][0][0].GetDouble());
        Assert.Equal("split", feature.GetProperty("properties").GetProperty("__name").GetString());
        Assert.Equal(0, feature.GetProperty("properties").GetProperty("__projects").GetArrayLength());
    }

    [Fact]
    public async Task RemovingAnOrdinaryAreaStillSendsItsPolygon()
    {
        var hole = """[[0.2,0.2],[0.4,0.2],[0.4,0.4],[0.2,0.2]]""";
        var single = $$"""{"type":"Polygon","coordinates":[{{SquareRingGeoJson}},{{hole}}]}""";
        var handler = new RoutingHandler(url => url.Contains("/area/", StringComparison.Ordinal)
            ? FeatureJson("Plain", single)
            : """{"status":"ok"}""");

        await CreateRoutingSut(handler).RemoveGeofenceFromProjectAsync("plain");

        var geometry = SavedFeature(Assert.Single(handler.Bodies)).GetProperty("geometry");
        Assert.Equal("Polygon", geometry.GetProperty("type").GetString());
        // Both rings go back, so the hole survives being taken out of the project.
        Assert.Equal(2, geometry.GetProperty("coordinates").GetArrayLength());
        Assert.Equal(1.0, geometry.GetProperty("coordinates")[0][1][0].GetDouble());
    }

    [Fact]
    public async Task RemovingAnAreaKojiHasNoGeometryForSavesNothing()
    {
        var handler = new RoutingHandler(url => url.Contains("/area/", StringComparison.Ordinal)
            ? """{"data":{"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[0.0,0.0]}}}"""
            : """{"status":"ok"}""");

        await CreateRoutingSut(handler).RemoveGeofenceFromProjectAsync("odd");

        Assert.Empty(handler.Bodies);
    }

    private static JsonElement SavedFeature(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("area").GetProperty("features")[0].Clone();
    }
}
