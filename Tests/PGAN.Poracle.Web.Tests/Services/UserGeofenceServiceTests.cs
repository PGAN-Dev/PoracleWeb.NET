using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class UserGeofenceServiceTests
{
    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<IKojiService> _kojiService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IHumanRepository> _humanRepo = new();
    private readonly Mock<IDiscordNotificationService> _discordNotificationService = new();
    private readonly Mock<ILogger<UserGeofenceService>> _logger = new();
    private readonly UserGeofenceService _sut;

    public UserGeofenceServiceTests()
    {
        this._sut = new UserGeofenceService(
            this._repository.Object,
            this._kojiService.Object,
            this._poracleApiProxy.Object,
            this._humanRepo.Object,
            this._discordNotificationService.Object,
            this._logger.Object);
    }

    // --- GetByUserAsync ---

    [Fact]
    public async Task GetByUserAsyncReturnsGeofencesFromRepositoryWithPolygons()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } };
        var polygonJson = System.Text.Json.JsonSerializer.Serialize(polygon);
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "downtown", PolygonJson = polygonJson }
        };
        this._repository.Setup(r => r.GetByHumanIdAsync("u1")).ReturnsAsync(geofences);

        var result = await this._sut.GetByUserAsync("u1");

        Assert.Single(result);
        Assert.Equal(polygon.Length, result[0].Polygon!.Length);
        Assert.Equal(polygon[0][0], result[0].Polygon![0][0]);
        Assert.Equal(polygon[0][1], result[0].Polygon![0][1]);
    }

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsyncStoresPolygonJsonOnRecord()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }, new[] { 5.0, 6.0 } };
        var model = new UserGeofenceCreate
        {
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Polygon = polygon
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        this._repository.Verify(r => r.CreateAsync(It.Is<UserGeofence>(g =>
            !string.IsNullOrEmpty(g.PolygonJson))), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncReturnsGeofenceWithCorrectFields()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "My Zone",
            GroupName = "Region",
            ParentId = 3,
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal("my zone", result.KojiName);
        Assert.Equal("My Zone", result.DisplayName);
        Assert.Equal("Region", result.GroupName);
        Assert.Equal(3, result.ParentId);
    }

    [Fact]
    public async Task CreateAsyncUpdatesHumanAreaJsonArray()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "Park",
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        var human = new Human { Id = "u1", Area = "[\"existing_area\"]" };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(human);
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        Assert.Contains("existing_area", human.Area);
        Assert.Contains("park", human.Area);
        this._humanRepo.Verify(r => r.UpdateAsync(human), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncCallsReloadGeofencesAsync()
    {
        var model = new UserGeofenceCreate { DisplayName = "Test", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncThrowsWhenLimitExceeded()
    {
        var model = new UserGeofenceCreate { DisplayName = "Over Limit", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(10);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.CreateAsync("u1", 1, model));

        Assert.Contains("Maximum of 10", ex.Message);
    }

    [Fact]
    public async Task CreateAsyncGeneratesKojiNameAsLowercaseDisplayName()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "My Cool Area!",
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal("my cool area!", result.KojiName);
    }

    [Fact]
    public async Task CreateAsyncUsesLowercaseDisplayNameAsKojiName()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "This Is A Very Long Display Name That Should Be Truncated",
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal(model.DisplayName.ToLowerInvariant(), result.KojiName);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsyncRemovesFromHumanAreaAndDeletesRecord()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        var human = new Human { Id = "u1", Area = "[\"downtown\",\"other_area\"]" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(human);
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.DeleteAsync("u1", 1, 1);

        Assert.DoesNotContain("downtown", human.Area);
        Assert.Contains("other_area", human.Area);
        this._repository.Verify(r => r.DeleteAsync(1), Times.Once);
        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncThrowsWhenGeofenceNotOwnedByUser()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "other_user", KojiName = "pweb_other_user_downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this._sut.DeleteAsync("u1", 1, 1));
    }

    [Fact]
    public async Task DeleteAsyncThrowsWhenGeofenceNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.DeleteAsync("u1", 1, 99));
    }

    // --- GetRegionsAsync ---

    [Fact]
    public async Task GetRegionsAsyncDelegatesToKojiService()
    {
        var regions = new List<GeofenceRegion>
        {
            new() { Id = 1, Name = "r1", DisplayName = "Region 1" }
        };
        this._kojiService.Setup(k => k.GetRegionsAsync()).ReturnsAsync(regions);

        var result = await this._sut.GetRegionsAsync();

        Assert.Equal(regions, result);
    }
}
