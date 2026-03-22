using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class GeofenceFeedControllerTests
{
    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<IKojiService> _kojiService = new();
    private readonly Mock<ILogger<GeofenceFeedController>> _logger = new();
    private readonly GeofenceFeedController _sut;

    public GeofenceFeedControllerTests()
    {
        this._kojiService.Setup(k => k.GetAdminGeofencesAsync()).ReturnsAsync([]);
        this._sut = new GeofenceFeedController(this._repository.Object, this._kojiService.Object, this._logger.Object);
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
}
