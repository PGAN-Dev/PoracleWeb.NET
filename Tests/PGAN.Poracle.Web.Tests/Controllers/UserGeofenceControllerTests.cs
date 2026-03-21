using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class UserGeofenceControllerTests : ControllerTestBase
{
    private readonly Mock<IUserGeofenceService> _service = new();
    private readonly Mock<ILogger<UserGeofenceController>> _logger = new();
    private readonly UserGeofenceController _sut;

    public UserGeofenceControllerTests()
    {
        this._sut = new UserGeofenceController(this._service.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    // --- GetCustomGeofences ---

    [Fact]
    public async Task GetCustomGeofencesReturnsOkWithList()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "pweb_123456789_downtown", DisplayName = "Downtown" }
        };
        this._service.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync(geofences);

        var result = await this._sut.GetCustomGeofences();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(geofences, ok.Value);
    }

    [Fact]
    public async Task GetCustomGeofencesReturnsOkWithEmptyList()
    {
        this._service.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync(new List<UserGeofence>());

        var result = await this._sut.GetCustomGeofences();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetCustomGeofencesUsesUserIdFromClaims()
    {
        SetupUser(this._sut, userId: "987654321", profileNo: 3);
        this._service.Setup(s => s.GetByUserAsync("987654321")).ReturnsAsync(new List<UserGeofence>());

        await this._sut.GetCustomGeofences();

        this._service.Verify(s => s.GetByUserAsync("987654321"), Times.Once);
    }

    // --- CreateGeofence ---

    [Fact]
    public async Task CreateGeofenceReturnsCreatedAtActionWithGeofence()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "My Area",
            GroupName = "Region1",
            ParentId = 5,
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        var created = new UserGeofence
        {
            Id = 0,
            KojiName = "pweb_123456789_my_area",
            DisplayName = "My Area"
        };
        this._service.Setup(s => s.CreateAsync("123456789", 1, model)).ReturnsAsync(created);

        var result = await this._sut.CreateGeofence(model);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(UserGeofenceController.GetCustomGeofences), createdResult.ActionName);
        Assert.Equal(created, createdResult.Value);
    }

    [Fact]
    public async Task CreateGeofenceReturnsBadRequestWhenLimitExceeded()
    {
        var model = new UserGeofenceCreate { DisplayName = "Too Many" };
        this._service.Setup(s => s.CreateAsync("123456789", 1, model))
            .ThrowsAsync(new InvalidOperationException("Maximum of 10 custom geofences per user reached."));

        var result = await this._sut.CreateGeofence(model);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateGeofenceUsesUserIdAndProfileNoFromClaims()
    {
        SetupUser(this._sut, userId: "555", profileNo: 2);
        var model = new UserGeofenceCreate { DisplayName = "Test" };
        this._service.Setup(s => s.CreateAsync("555", 2, model)).ReturnsAsync(new UserGeofence());

        await this._sut.CreateGeofence(model);

        this._service.Verify(s => s.CreateAsync("555", 2, model), Times.Once);
    }

    // --- DeleteGeofence ---

    [Fact]
    public async Task DeleteGeofenceReturnsNoContent()
    {
        this._service.Setup(s => s.DeleteAsync("123456789", 1, 42)).Returns(Task.CompletedTask);

        var result = await this._sut.DeleteGeofence(42);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteGeofenceReturnsNotFoundWhenNotExists()
    {
        this._service.Setup(s => s.DeleteAsync("123456789", 1, 99))
            .ThrowsAsync(new InvalidOperationException("Geofence with ID 99 not found."));

        var result = await this._sut.DeleteGeofence(99);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteGeofenceReturnsForbidWhenNotOwned()
    {
        this._service.Setup(s => s.DeleteAsync("123456789", 1, 42))
            .ThrowsAsync(new UnauthorizedAccessException("Geofence does not belong to this user."));

        var result = await this._sut.DeleteGeofence(42);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteGeofenceUsesUserIdAndProfileNoFromClaims()
    {
        SetupUser(this._sut, userId: "777", profileNo: 4);
        this._service.Setup(s => s.DeleteAsync("777", 4, 10)).Returns(Task.CompletedTask);

        await this._sut.DeleteGeofence(10);

        this._service.Verify(s => s.DeleteAsync("777", 4, 10), Times.Once);
    }

    // --- SubmitForReview ---

    [Fact]
    public async Task SubmitForReviewReturnsOkWithUpdatedGeofence()
    {
        var updated = new UserGeofence { Id = 1, KojiName = "pweb_123456789_downtown", Status = "pending_review" };
        this._service.Setup(s => s.SubmitForReviewAsync("123456789", "pweb_123456789_downtown")).ReturnsAsync(updated);

        var result = await this._sut.SubmitForReview("pweb_123456789_downtown");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(updated, ok.Value);
    }

    [Fact]
    public async Task SubmitForReviewReturnsBadRequestWhenInvalidStatus()
    {
        this._service.Setup(s => s.SubmitForReviewAsync("123456789", "pweb_123456789_downtown"))
            .ThrowsAsync(new InvalidOperationException("Geofence must be in 'active' status."));

        var result = await this._sut.SubmitForReview("pweb_123456789_downtown");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitForReviewReturnsForbidWhenNotOwned()
    {
        this._service.Setup(s => s.SubmitForReviewAsync("123456789", "pweb_other_downtown"))
            .ThrowsAsync(new UnauthorizedAccessException("Geofence does not belong to this user."));

        var result = await this._sut.SubmitForReview("pweb_other_downtown");

        Assert.IsType<ForbidResult>(result);
    }

    // --- GetRegions ---

    [Fact]
    public async Task GetRegionsReturnsOkWithRegionList()
    {
        var regions = new List<GeofenceRegion>
        {
            new() { Id = 1, Name = "region1", DisplayName = "Region 1" },
            new() { Id = 2, Name = "region2", DisplayName = "Region 2" }
        };
        this._service.Setup(s => s.GetRegionsAsync()).ReturnsAsync(regions);

        var result = await this._sut.GetRegions();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(regions, ok.Value);
    }

    [Fact]
    public async Task GetRegionsReturnsEmptyArrayWhenServiceThrows()
    {
        this._service.Setup(s => s.GetRegionsAsync()).ThrowsAsync(new Exception("Koji unreachable"));

        var result = await this._sut.GetRegions();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }
}
