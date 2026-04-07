using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class GeoJsonServiceTests
{
    private readonly Mock<IUserGeofenceService> _userGeofenceService = new();
    private readonly GeoJsonService _sut;

    public GeoJsonServiceTests() => this._sut = new GeoJsonService(
        this._userGeofenceService.Object,
        NullLogger<GeoJsonService>.Instance);

    private static MemoryStream ToStream(string json) =>
        new(Encoding.UTF8.GetBytes(json));

    private static string MakeFeatureCollection(params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";

    private static string MakePolygonFeature(double[][] lonLatRing, string? name = null)
    {
        var coordsJson = JsonSerializer.Serialize(new[] { lonLatRing });
        var propsJson = name is not null
            ? ",\"properties\":{\"name\":\"" + name + "\"}"
            : string.Empty;
        return "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":" + coordsJson + "}" + propsJson + "}";
    }

    // A simple triangle in GeoJSON [lon, lat] order, with closing vertex
    private static double[][] TriangleLonLat() =>
    [
        [10.0, 20.0],
        [11.0, 21.0],
        [12.0, 20.0],
        [10.0, 20.0]
    ];

    // A simple triangle in internal [lat, lon] order, no closing vertex
    private static double[][] TriangleLatLon() =>
    [
        [20.0, 10.0],
        [21.0, 11.0],
        [20.0, 12.0]
    ];

    // ───────────── Export Tests ─────────────

    [Fact]
    public async Task ExportAsyncIncludesUserGeofences()
    {
        this._userGeofenceService.Setup(u => u.GetByUserAsync("u1")).ReturnsAsync(
        [
            new UserGeofence { KojiName = "user1", DisplayName = "User 1", GroupName = "ug1", Polygon = TriangleLatLon() },
            new UserGeofence { KojiName = "user2", DisplayName = "User 2", GroupName = "ug2", Polygon = TriangleLatLon() }
        ]);

        var result = await this._sut.ExportAsync("u1");

        Assert.Equal("FeatureCollection", result.Type);
        Assert.Equal(2, result.Features.Count);
        Assert.Equal("user", result.Features[0].Properties!["source"].GetString());
        Assert.Equal("user1", result.Features[0].Properties!["name"].GetString());
        Assert.Equal("user2", result.Features[1].Properties!["name"].GetString());
    }

    [Fact]
    public async Task ExportAsyncConvertsLatLonToGeoJsonLonLat()
    {
        this._userGeofenceService.Setup(u => u.GetByUserAsync("u1")).ReturnsAsync(
        [
            new UserGeofence
            {
                KojiName = "test", DisplayName = "Test", GroupName = "",
                Polygon = [[40.0, -74.0], [41.0, -73.0], [40.5, -74.5]]
            }
        ]);

        var result = await this._sut.ExportAsync("u1");
        var coords = result.Features[0].Geometry.Coordinates;

        // Polygon coordinates: [ [ [lon,lat], ... ] ]
        var ring = coords[0];
        // First point: internal [40, -74] becomes GeoJSON [-74, 40]
        Assert.Equal(-74.0, ring[0][0].GetDouble());
        Assert.Equal(40.0, ring[0][1].GetDouble());
    }

    [Fact]
    public async Task ExportAsyncClosesRings()
    {
        // Open ring (first != last)
        this._userGeofenceService.Setup(u => u.GetByUserAsync("u1")).ReturnsAsync(
        [
            new UserGeofence
            {
                KojiName = "open", DisplayName = "Open", GroupName = "",
                Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
            }
        ]);

        var result = await this._sut.ExportAsync("u1");
        var ring = result.Features[0].Geometry.Coordinates[0];

        // Should have 4 points (3 + closing vertex)
        Assert.Equal(4, ring.GetArrayLength());
        // Last point should equal first
        Assert.Equal(ring[0][0].GetDouble(), ring[3][0].GetDouble());
        Assert.Equal(ring[0][1].GetDouble(), ring[3][1].GetDouble());
    }

    [Fact]
    public async Task ExportAsyncSkipsEmptyPolygons()
    {
        this._userGeofenceService.Setup(u => u.GetByUserAsync("u1")).ReturnsAsync(
        [
            new UserGeofence { KojiName = "empty1", DisplayName = "Empty1", GroupName = "", Polygon = null },
            new UserGeofence { KojiName = "empty2", DisplayName = "Empty2", GroupName = "", Polygon = [] },
            new UserGeofence { KojiName = "valid", DisplayName = "Valid", GroupName = "", Polygon = TriangleLatLon() }
        ]);

        var result = await this._sut.ExportAsync("u1");

        Assert.Single(result.Features);
        Assert.Equal("valid", result.Features[0].Properties!["name"].GetString());
    }

    // ───────────── Import Tests ─────────────

    [Fact]
    public async Task ImportAsyncParsesFeatureCollection()
    {
        var triangle = TriangleLonLat();
        var json = MakeFeatureCollection(
            MakePolygonFeature(triangle, "Fence A"),
            MakePolygonFeature(triangle, "Fence B"));

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ReturnsAsync((string _, int _, UserGeofenceCreate c) =>
                new UserGeofence { DisplayName = c.DisplayName });

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Equal(2, result.Created.Count);
        Assert.Empty(result.Errors);
        Assert.Equal("Fence A", result.Created[0].DisplayName);
        Assert.Equal("Fence B", result.Created[1].DisplayName);
    }

    [Fact]
    public async Task ImportAsyncParsesSingleFeature()
    {
        var json = MakePolygonFeature(TriangleLonLat(), "Solo");

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ReturnsAsync((string _, int _, UserGeofenceCreate c) =>
                new UserGeofence { DisplayName = c.DisplayName });

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Single(result.Created);
        Assert.Equal("Solo", result.Created[0].DisplayName);
    }

    [Fact]
    public async Task ImportAsyncConvertsGeoJsonLonLatToInternalLatLon()
    {
        // GeoJSON point: [lon=10, lat=20]
        var json = MakeFeatureCollection(MakePolygonFeature(TriangleLonLat(), "Conv"));

        UserGeofenceCreate? captured = null;
        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .Callback<string, int, UserGeofenceCreate>((_, _, c) => captured = c)
            .ReturnsAsync(new UserGeofence { DisplayName = "Conv" });

        await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.NotNull(captured);
        // GeoJSON [10, 20] -> internal [20, 10]
        Assert.Equal(20.0, captured!.Polygon[0][0]);
        Assert.Equal(10.0, captured.Polygon[0][1]);
    }

    [Fact]
    public async Task ImportAsyncRemovesClosingVertex()
    {
        var json = MakeFeatureCollection(MakePolygonFeature(TriangleLonLat(), "Closed"));

        UserGeofenceCreate? captured = null;
        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .Callback<string, int, UserGeofenceCreate>((_, _, c) => captured = c)
            .ReturnsAsync(new UserGeofence { DisplayName = "Closed" });

        await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.NotNull(captured);
        // TriangleLonLat has 4 points (closed); after removing closing vertex, should be 3
        Assert.Equal(3, captured!.Polygon.Length);
    }

    [Fact]
    public async Task ImportAsyncRejectsInvalidCoordinates()
    {
        // Latitude out of range (lat=200 after lon/lat swap)
        double[][] badRing = [[10.0, 200.0], [11.0, 201.0], [12.0, 200.0], [10.0, 200.0]];
        var json = MakeFeatureCollection(MakePolygonFeature(badRing, "BadCoords"));

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Empty(result.Created);
        Assert.Single(result.Errors);
        Assert.Contains("out of valid range", result.Errors[0].Reason);
    }

    [Fact]
    public async Task ImportAsyncRejectsTooFewPoints()
    {
        // Only 2 unique points + closing = 3 total, but after removing closing = 2 < MinPoints(3)
        double[][] twoPoints = [[10.0, 20.0], [11.0, 21.0], [10.0, 20.0]];
        var json = MakeFeatureCollection(MakePolygonFeature(twoPoints, "TooFew"));

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Empty(result.Created);
        Assert.Single(result.Errors);
        Assert.Contains("minimum is 3", result.Errors[0].Reason);
    }

    [Fact]
    public async Task ImportAsyncRejectsTooManyPoints()
    {
        // Generate 502 points (501 unique + closing)
        var ring = new double[502][];
        for (var i = 0; i < 501; i++)
        {
            ring[i] = [i * 0.001, 20.0 + (i * 0.001)];
        }
        ring[501] = ring[0];

        var json = MakeFeatureCollection(MakePolygonFeature(ring, "TooMany"));

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Empty(result.Created);
        Assert.Single(result.Errors);
        Assert.Contains("maximum is 500", result.Errors[0].Reason);
    }

    [Fact]
    public async Task ImportAsyncHandlesMultiPolygon()
    {
        var ring = TriangleLonLat();
        var coordsJson = JsonSerializer.Serialize(new[] { new[] { ring } });
        var json = "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + "\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":" + coordsJson + "},"
            + "\"properties\":{\"name\":\"Multi\"}}]}";

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ReturnsAsync(new UserGeofence { DisplayName = "Multi" });

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Single(result.Created);
        Assert.Equal("Multi", result.Created[0].DisplayName);
    }

    [Fact]
    public async Task ImportAsyncSkipsUnsupportedGeometryTypes()
    {
        var json = /*lang=json,strict*/ """
        {
            "type": "FeatureCollection",
            "features": [{
                "type": "Feature",
                "geometry": {"type": "Point", "coordinates": [10, 20]},
                "properties": {"name": "APoint"}
            }, {
                "type": "Feature",
                "geometry": {"type": "LineString", "coordinates": [[10, 20], [11, 21]]},
                "properties": {"name": "ALine"}
            }]
        }
        """;

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Empty(result.Created);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains("Unsupported geometry type", e.Reason));
    }

    [Fact]
    public async Task ImportAsyncExtractsNameFromProperties()
    {
        // Test fallback: __name when name is absent, then auto-generated
        var ring = TriangleLonLat();
        var coordsJson = JsonSerializer.Serialize(new[] { ring });

        var feat1 = "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":" + coordsJson + "},\"properties\":{\"name\":\"Primary\"}}";
        var feat2 = "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":" + coordsJson + "},\"properties\":{\"__name\":\"Fallback\"}}";
        var feat3 = "{\"type\":\"Feature\",\"geometry\":{\"type\":\"Polygon\",\"coordinates\":" + coordsJson + "},\"properties\":{}}";
        var json = MakeFeatureCollection(feat1, feat2, feat3);

        var names = new List<string>();
        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .Callback<string, int, UserGeofenceCreate>((_, _, c) => names.Add(c.DisplayName))
            .ReturnsAsync(new UserGeofence());

        await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Equal(3, names.Count);
        Assert.Equal("Primary", names[0]);
        Assert.Equal("Fallback", names[1]);
        Assert.Equal("Imported 3", names[2]);
    }

    [Fact]
    public async Task ImportAsyncHandlesPartialFailure()
    {
        var triangle = TriangleLonLat();
        double[][] twoPoints = [[10.0, 20.0], [11.0, 21.0], [10.0, 20.0]];

        var json = MakeFeatureCollection(
            MakePolygonFeature(triangle, "Good"),
            MakePolygonFeature(twoPoints, "Bad"),
            MakePolygonFeature(triangle, "AlsoGood"));

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ReturnsAsync((string _, int _, UserGeofenceCreate c) =>
                new UserGeofence { DisplayName = c.DisplayName });

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Equal(2, result.Created.Count);
        Assert.Single(result.Errors);
        Assert.Equal("Bad", result.Errors[0].FeatureName);
    }

    [Fact]
    public async Task ImportAsyncEnforcesMaxFeatureLimit()
    {
        var triangle = TriangleLonLat();
        var features = Enumerable.Range(0, 55)
            .Select(i => MakePolygonFeature(triangle, $"F{i}"))
            .ToArray();
        var json = MakeFeatureCollection(features);

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ReturnsAsync((string _, int _, UserGeofenceCreate c) =>
                new UserGeofence { DisplayName = c.DisplayName });

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Equal(50, result.Created.Count);
        Assert.Single(result.Errors);
        Assert.Contains("5 features were skipped", result.Errors[0].Reason);
    }

    [Fact]
    public async Task ImportAsyncRejectsInvalidJson()
    {
        var json = "this is not json at all";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.ImportAsync("u1", 1, ToStream(json)));
    }

    [Fact]
    public async Task ImportAsyncTruncatesLongDisplayNames()
    {
        var longName = new string('A', 80);
        var json = MakeFeatureCollection(MakePolygonFeature(TriangleLonLat(), longName));

        UserGeofenceCreate? captured = null;
        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .Callback<string, int, UserGeofenceCreate>((_, _, c) => captured = c)
            .ReturnsAsync(new UserGeofence { DisplayName = "truncated" });

        await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.NotNull(captured);
        Assert.Equal(50, captured!.DisplayName.Length);
    }

    [Fact]
    public async Task ImportAsyncCreateAsyncThrowsRecordsError()
    {
        var json = MakeFeatureCollection(MakePolygonFeature(TriangleLonLat(), "MaxedOut"));

        this._userGeofenceService
            .Setup(u => u.CreateAsync("u1", 1, It.IsAny<UserGeofenceCreate>()))
            .ThrowsAsync(new InvalidOperationException("Maximum 10 custom geofences"));

        var result = await this._sut.ImportAsync("u1", 1, ToStream(json));

        Assert.Empty(result.Created);
        Assert.Single(result.Errors);
        Assert.Contains("Maximum 10", result.Errors[0].Reason);
    }
}
