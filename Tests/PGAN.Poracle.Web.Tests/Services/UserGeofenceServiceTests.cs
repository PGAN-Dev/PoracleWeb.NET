using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class UserGeofenceServiceTests
{
    private readonly Mock<IUserGeofenceRepository> _geofenceRepo = new();
    private readonly Mock<IKojiService> _kojiService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IHumanRepository> _humanRepo = new();
    private readonly Mock<ILogger<UserGeofenceService>> _logger = new();
    private readonly UserGeofenceService _sut;

    public UserGeofenceServiceTests()
    {
        this._sut = new UserGeofenceService(
            this._geofenceRepo.Object,
            this._kojiService.Object,
            this._poracleApiProxy.Object,
            this._humanRepo.Object,
            this._logger.Object);
    }

    // --- GetByUserAsync ---

    [Fact]
    public async Task GetByUserAsyncDelegatesToRepository()
    {
        var expected = new List<UserGeofence> { new() { Id = 1 } };
        this._geofenceRepo.Setup(r => r.GetByHumanIdAsync("u1", 1)).ReturnsAsync(expected);

        var result = await this._sut.GetByUserAsync("u1", 1);

        Assert.Equal(expected, result);
    }

    // --- CreateAsync ---

    [Fact]
    public async Task CreateAsyncCallsKojiServiceWithCorrectParams()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }, new[] { 5.0, 6.0 } };
        var model = new UserGeofenceCreate
        {
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Polygon = polygon
        };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        this._kojiService.Verify(k => k.SaveGeofenceAsync("pweb_u1_downtown", "Downtown", "City", 5, polygon), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncCreatesLocalRecordWithCorrectFields()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "My Zone",
            GroupName = "Region",
            ParentId = 3,
            Polygon = [[1.0, 2.0], [3.0, 4.0]]
        };
        UserGeofence? captured = null;
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .Callback<UserGeofence>(g => captured = g)
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        Assert.NotNull(captured);
        Assert.Equal("u1", captured.HumanId);
        Assert.Equal(1, captured.ProfileNo);
        Assert.Equal("pweb_u1_my_zone", captured.GeofenceName);
        Assert.Equal("My Zone", captured.DisplayName);
        Assert.Equal("Region", captured.GroupName);
        Assert.Equal(3, captured.ParentId);
    }

    [Fact]
    public async Task CreateAsyncUpdatesHumanAreaJsonArray()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "Park",
            Polygon = [[1.0, 2.0]]
        };
        var human = new Human { Id = "u1", Area = "[\"existing_area\"]" };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(human);
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        Assert.Contains("existing_area", human.Area);
        Assert.Contains("pweb_u1_park", human.Area);
        this._humanRepo.Verify(r => r.UpdateAsync(human), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncCallsReloadGeofencesAsync()
    {
        var model = new UserGeofenceCreate { DisplayName = "Test", Polygon = [[1.0, 2.0]] };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.CreateAsync("u1", 1, model);

        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncThrowsWhenLimitExceeded()
    {
        var model = new UserGeofenceCreate { DisplayName = "Over Limit", Polygon = [[1.0, 2.0]] };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(10);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.CreateAsync("u1", 1, model));

        Assert.Contains("Maximum of 10", ex.Message);
    }

    [Fact]
    public async Task CreateAsyncGeneratesCorrectSlugFromDisplayName()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "My Cool Area!",
            Polygon = [[1.0, 2.0]]
        };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal("pweb_u1_my_cool_area", result.GeofenceName);
    }

    [Fact]
    public async Task CreateAsyncTruncatesLongSlug()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "This Is A Very Long Display Name That Should Be Truncated",
            Polygon = [[1.0, 2.0]]
        };
        this._geofenceRepo.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._geofenceRepo.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Area = "[]" });
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        var result = await this._sut.CreateAsync("u1", 1, model);

        // Slug part (after pweb_u1_) should be at most 30 characters
        var slug = result.GeofenceName.Replace("pweb_u1_", "");
        Assert.True(slug.Length <= 30);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsyncRemovesFromKojiAreaAndLocalDb()
    {
        var existing = new UserGeofence
        {
            Id = 5,
            HumanId = "u1",
            GeofenceName = "pweb_u1_downtown",
            PolygonJson = "[[1.0,2.0],[3.0,4.0],[5.0,6.0]]"
        };
        var human = new Human { Id = "u1", Area = "[\"pweb_u1_downtown\",\"other_area\"]" };
        this._geofenceRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(human);
        this._humanRepo.Setup(r => r.UpdateAsync(It.IsAny<Human>())).ReturnsAsync((Human h) => h);

        await this._sut.DeleteAsync("u1", 1, 5);

        this._kojiService.Verify(k => k.RemoveGeofenceFromProjectAsync("pweb_u1_downtown", It.IsAny<double[][]>()), Times.Once);
        this._geofenceRepo.Verify(r => r.DeleteAsync(5), Times.Once);
        Assert.DoesNotContain("pweb_u1_downtown", human.Area);
        Assert.Contains("other_area", human.Area);
        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncThrowsWhenGeofenceNotFound()
    {
        this._geofenceRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.DeleteAsync("u1", 1, 999));
    }

    [Fact]
    public async Task DeleteAsyncThrowsWhenGeofenceNotOwnedByUser()
    {
        var existing = new UserGeofence { Id = 5, HumanId = "other_user", GeofenceName = "pweb_other_downtown" };
        this._geofenceRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(existing);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this._sut.DeleteAsync("u1", 1, 5));
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsyncUpdatesKojiAndLocalRecord()
    {
        var existing = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            GeofenceName = "pweb_u1_old",
            DisplayName = "Old",
            GroupName = "OldGroup",
            ParentId = 1,
            PolygonJson = "[[1.0,2.0]]"
        };
        var model = new UserGeofenceCreate
        {
            DisplayName = "Updated Name",
            GroupName = "NewGroup",
            ParentId = 7,
            Polygon = [[10.0, 20.0], [30.0, 40.0]]
        };
        this._geofenceRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        this._geofenceRepo.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);

        var result = await this._sut.UpdateAsync("u1", 1, model);

        this._kojiService.Verify(k => k.SaveGeofenceAsync("pweb_u1_old", "Updated Name", "NewGroup", 7, model.Polygon), Times.Once);
        Assert.Equal("Updated Name", result.DisplayName);
        Assert.Equal("NewGroup", result.GroupName);
        Assert.Equal(7, result.ParentId);
        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsyncThrowsWhenGeofenceNotFound()
    {
        this._geofenceRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((UserGeofence?)null);
        var model = new UserGeofenceCreate { DisplayName = "X" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.UpdateAsync("u1", 999, model));
    }

    [Fact]
    public async Task UpdateAsyncThrowsWhenNotOwnedByUser()
    {
        var existing = new UserGeofence { Id = 1, HumanId = "other_user" };
        this._geofenceRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        var model = new UserGeofenceCreate { DisplayName = "X" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this._sut.UpdateAsync("u1", 1, model));
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
