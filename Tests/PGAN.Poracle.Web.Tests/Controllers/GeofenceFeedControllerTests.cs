using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class GeofenceFeedControllerTests
{
    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<ILogger<GeofenceFeedController>> _logger = new();
    private readonly GeofenceFeedController _sut;

    public GeofenceFeedControllerTests()
    {
        this._sut = new GeofenceFeedController(this._repository.Object, this._logger.Object);
    }

    [Fact]
    public async Task GetPoracleFeedReturnsOkWithDataArray()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }, new[] { 5.0, 6.0 } };
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
        var twoPoints = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } };
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
    public async Task GetPoracleFeedResponseFormatMatchesPoracleExpectations()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }, new[] { 5.0, 6.0 } };
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

        // Root has status and data
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());

        // Each item has the expected Poracle fields
        var item = data[0];
        Assert.Equal("my_area", item.GetProperty("name").GetString());
        Assert.False(item.GetProperty("userSelectable").GetBoolean());
        Assert.False(item.GetProperty("displayInMatches").GetBoolean());
        Assert.Equal(3, item.GetProperty("path").GetArrayLength());
    }

    [Fact]
    public async Task GetPoracleFeedIncludesMultipleValidGeofences()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }, new[] { 5.0, 6.0 } };
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
}
