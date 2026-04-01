using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class UserGeofenceServiceTests
{
    private readonly Mock<IUserGeofenceRepository> _repository = new();
    private readonly Mock<IKojiService> _kojiService = new();
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IPoracleServerService> _poracleServerService = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IHumanRepository> _humanRepo = new();
    private readonly Mock<IProfileRepository> _profileRepo = new();
    private readonly Mock<IDiscordNotificationService> _discordNotificationService = new();
    private readonly Mock<ILogger<UserGeofenceService>> _logger = new();
    private readonly UserGeofenceService _sut;

    public UserGeofenceServiceTests() => this._sut = new UserGeofenceService(
            this._repository.Object,
            this._kojiService.Object,
            this._poracleApiProxy.Object,
            this._poracleServerService.Object,
            this._humanProxy.Object,
            this._humanRepo.Object,
            this._profileRepo.Object,
            this._discordNotificationService.Object,
            this._logger.Object);

    /// <summary>
    /// Helper: creates a JsonElement matching what IPoracleHumanProxy.GetHumanAsync returns.
    /// </summary>
    private static JsonElement MakeHumanJson(string id, string area = "[]") =>
        JsonSerializer.SerializeToElement(new { id, area, latitude = 0.0, longitude = 0.0, enabled = 1, current_profile_no = 1 });

    // --- GetByUserAsync ---

    [Fact]
    public async Task GetByUserAsyncReturnsGeofencesFromRepositoryWithPolygons()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0] };
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
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
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
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[]"));

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
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[]"));

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
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"existing_area\"]"));

        await this._sut.CreateAsync("u1", 1, model);

        // Verify the proxy was called with both the existing area and the new one
        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            a.Contains("existing_area") && a.Contains("park"))), Times.Once);
    }

    [Fact]
    public async Task CreateAsyncCallsReloadGeofencesAsync()
    {
        var model = new UserGeofenceCreate { DisplayName = "Test", Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]] };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[]"));

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
            DisplayName = "My Cool Area",
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[]"));

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal("my cool area", result.KojiName);
    }

    [Fact]
    public async Task CreateAsyncUsesLowercaseDisplayNameAsKojiName()
    {
        var model = new UserGeofenceCreate
        {
            DisplayName = "Long Display Name For Testing",
            Polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]]
        };
        this._repository.Setup(r => r.GetCountByHumanIdAsync("u1")).ReturnsAsync(0);
        this._repository.Setup(r => r.CreateAsync(It.IsAny<UserGeofence>()))
            .ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[]"));

        var result = await this._sut.CreateAsync("u1", 1, model);

        Assert.Equal(model.DisplayName.ToLowerInvariant(), result.KojiName);
    }

    // --- DeleteAsync ---

    [Fact]
    public async Task DeleteAsyncRemovesFromHumanAreaAndDeletesRecord()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\",\"other_area\"]"));

        await this._sut.DeleteAsync("u1", 1, 1);

        // Verify proxy was called to set areas without "downtown"
        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            !a.Contains("downtown") && a.Contains("other_area"))), Times.Once);
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

    [Fact]
    public async Task DeleteAsyncRemovesAreaFromAllProfiles()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        var profile1 = new Profile { Id = "u1", ProfileNo = 1, Area = "[\"downtown\",\"park\"]" };
        var profile2 = new Profile { Id = "u1", ProfileNo = 2, Area = "[\"downtown\"]" };
        var profile3 = new Profile { Id = "u1", ProfileNo = 3, Area = "[\"park\"]" };

        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        // Active profile removal goes through proxy
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\",\"other\"]"));
        // Non-active profile cleanup goes through direct DB
        this._profileRepo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([profile1, profile2, profile3]);
        this._profileRepo.Setup(r => r.UpdateAsync(It.IsAny<Profile>())).ReturnsAsync((Profile p) => p);

        await this._sut.DeleteAsync("u1", 1, 1);

        // Proxy should be called to remove "downtown" from active profile areas
        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            !a.Contains("downtown") && a.Contains("other"))), Times.Once);
        // Profile 1 and 2 had "downtown" and should be updated via direct DB
        Assert.DoesNotContain("downtown", profile1.Area);
        Assert.DoesNotContain("downtown", profile2.Area);
        Assert.Contains("park", profile1.Area);
        // Profile 3 never had "downtown", should not be updated
        this._profileRepo.Verify(r => r.UpdateAsync(profile3), Times.Never);
        this._profileRepo.Verify(r => r.UpdateAsync(It.IsAny<Profile>()), Times.Exactly(2));
    }

    // --- SubmitForReviewAsync ---

    [Fact]
    public async Task SubmitForReviewAsyncUpdatesStatusToPendingReview()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "active", PolygonJson = "[[1,2],[3,4],[5,6]]" };
        this._repository.Setup(r => r.GetByKojiNameAsync("downtown")).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Name = "TestUser" });
        this._discordNotificationService.Setup(d => d.CreateGeofenceSubmissionPostAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        var result = await this._sut.SubmitForReviewAsync("u1", "downtown");

        Assert.Equal("pending_review", result.Status);
        Assert.NotNull(result.SubmittedAt);
    }

    [Fact]
    public async Task SubmitForReviewAsyncThrowsWhenNotOwned()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "other_user", KojiName = "downtown", Status = "active" };
        this._repository.Setup(r => r.GetByKojiNameAsync("downtown")).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => this._sut.SubmitForReviewAsync("u1", "downtown"));
    }

    [Fact]
    public async Task SubmitForReviewAsyncThrowsWhenNotActiveStatus()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "pending_review" };
        this._repository.Setup(r => r.GetByKojiNameAsync("downtown")).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.SubmitForReviewAsync("u1", "downtown"));
    }

    [Fact]
    public async Task SubmitForReviewAsyncThrowsWhenGeofenceNotFound()
    {
        this._repository.Setup(r => r.GetByKojiNameAsync("missing")).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.SubmitForReviewAsync("u1", "missing"));
    }

    [Fact]
    public async Task SubmitForReviewAsyncSavesDiscordThreadId()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", DisplayName = "Downtown", GroupName = "City", Status = "active", PolygonJson = "[[1,2],[3,4],[5,6]]" };
        this._repository.Setup(r => r.GetByKojiNameAsync("downtown")).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._humanRepo.Setup(r => r.GetByIdAndProfileAsync("u1", 1)).ReturnsAsync(new Human { Id = "u1", Name = "TestUser" });
        this._discordNotificationService.Setup(d => d.CreateGeofenceSubmissionPostAsync(
            "u1", "TestUser", "Downtown", "City", 3, It.IsAny<string?>()))
            .ReturnsAsync("thread_123");

        var result = await this._sut.SubmitForReviewAsync("u1", "downtown");

        Assert.Equal("thread_123", result.DiscordThreadId);
        this._repository.Verify(r => r.UpdateAsync(It.Is<UserGeofence>(g => g.DiscordThreadId == "thread_123")), Times.AtLeastOnce);
    }

    // --- ApproveSubmissionAsync ---

    [Fact]
    public async Task ApproveSubmissionAsyncSavesToKojiAndUpdatesStatus()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Status = "pending_review",
            PolygonJson = System.Text.Json.JsonSerializer.Serialize(polygon)
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);

        var result = await this._sut.ApproveSubmissionAsync("admin1", 1, null);

        Assert.Equal("approved", result.Status);
        Assert.Equal("admin1", result.ReviewedBy);
        Assert.NotNull(result.ReviewedAt);
        this._kojiService.Verify(k => k.SaveGeofenceAsync("downtown", "Downtown", "City", 5, It.IsAny<double[][]>(), true), Times.Once);
        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task ApproveSubmissionAsyncUsesPromotedNameForKoji()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Status = "pending_review",
            PolygonJson = System.Text.Json.JsonSerializer.Serialize(polygon)
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\"]"));

        var result = await this._sut.ApproveSubmissionAsync("admin1", 1, "Downtown Official");

        Assert.Equal("Downtown Official", result.PromotedName);
        this._kojiService.Verify(k => k.SaveGeofenceAsync("Downtown Official", "Downtown", "City", 5, It.IsAny<double[][]>(), true), Times.Once);
    }

    [Fact]
    public async Task ApproveSubmissionAsyncSwapsAreaNameWhenPromotedNameDiffers()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Status = "pending_review",
            PolygonJson = System.Text.Json.JsonSerializer.Serialize(polygon)
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\",\"other\"]"));

        await this._sut.ApproveSubmissionAsync("admin1", 1, "New Downtown");

        // Verify proxy was called with swapped area names
        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            !a.Contains("downtown") && a.Contains("new downtown") && a.Contains("other"))), Times.Once);
    }

    [Fact]
    public async Task ApproveSubmissionAsyncThrowsWhenPromotedNameHasInvalidChars() => await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.ApproveSubmissionAsync("admin1", 1, "test<script>"));

    [Fact]
    public async Task ApproveSubmissionAsyncThrowsWhenPromotedNameTooLong()
    {
        var longName = new string('a', 51);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.ApproveSubmissionAsync("admin1", 1, longName));
    }

    [Fact]
    public async Task ApproveSubmissionAsyncThrowsWhenPromotedNameIsEmpty() => await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.ApproveSubmissionAsync("admin1", 1, "   "));

    [Fact]
    public async Task ApproveSubmissionAsyncAcceptsValidPromotedName()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            GroupName = "City",
            ParentId = 5,
            Status = "pending_review",
            PolygonJson = System.Text.Json.JsonSerializer.Serialize(polygon)
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\"]"));

        var result = await this._sut.ApproveSubmissionAsync("admin1", 1, "Valid Name's (Test)");

        Assert.Equal("approved", result.Status);
        Assert.Equal("Valid Name's (Test)", result.PromotedName);
    }

    [Fact]
    public async Task ApproveSubmissionAsyncThrowsWhenNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.ApproveSubmissionAsync("admin1", 99, null));
    }

    [Fact]
    public async Task ApproveSubmissionAsyncThrowsWhenNoPolygonData()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "pending_review", PolygonJson = null };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.ApproveSubmissionAsync("admin1", 1, null));
    }

    [Fact]
    public async Task ApproveSubmissionAsyncPostsDiscordApprovalWhenThreadExists()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0] };
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            GroupName = "City",
            Status = "pending_review",
            DiscordThreadId = "thread_456",
            PolygonJson = System.Text.Json.JsonSerializer.Serialize(polygon)
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);

        await this._sut.ApproveSubmissionAsync("admin1", 1, null);

        this._discordNotificationService.Verify(d => d.PostApprovalMessageAsync("thread_456", "Downtown", "Downtown"), Times.Once);
    }

    // --- RejectSubmissionAsync ---

    [Fact]
    public async Task RejectSubmissionAsyncUpdatesStatusAndNotes()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", DisplayName = "Downtown", Status = "pending_review" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);

        var result = await this._sut.RejectSubmissionAsync("admin1", 1, "Too large");

        Assert.Equal("rejected", result.Status);
        Assert.Equal("admin1", result.ReviewedBy);
        Assert.Equal("Too large", result.ReviewNotes);
        Assert.NotNull(result.ReviewedAt);
    }

    [Fact]
    public async Task RejectSubmissionAsyncThrowsWhenNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.RejectSubmissionAsync("admin1", 99, "Reason"));
    }

    [Fact]
    public async Task RejectSubmissionAsyncPostsDiscordRejectionWhenThreadExists()
    {
        var geofence = new UserGeofence
        {
            Id = 1,
            HumanId = "u1",
            KojiName = "downtown",
            DisplayName = "Downtown",
            Status = "pending_review",
            DiscordThreadId = "thread_789"
        };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);

        await this._sut.RejectSubmissionAsync("admin1", 1, "Overlaps existing");

        this._discordNotificationService.Verify(d => d.PostRejectionMessageAsync("thread_789", "Downtown", "Overlaps existing"), Times.Once);
    }

    [Fact]
    public async Task RejectSubmissionAsyncSkipsDiscordWhenNoThreadId()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "pending_review" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<UserGeofence>())).ReturnsAsync((UserGeofence g) => g);

        await this._sut.RejectSubmissionAsync("admin1", 1, "No good");

        this._discordNotificationService.Verify(d => d.PostRejectionMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // --- AdminDeleteAsync ---

    [Fact]
    public async Task AdminDeleteAsyncDeletesFromDbAndReloadsGeofences()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "active" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\"]"));

        await this._sut.AdminDeleteAsync("admin1", 1);

        this._repository.Verify(r => r.DeleteAsync(1), Times.Once);
        this._poracleApiProxy.Verify(p => p.ReloadGeofencesAsync(), Times.Once);
    }

    [Fact]
    public async Task AdminDeleteAsyncRemovesFromKojiWhenApproved()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "approved", PromotedName = "Downtown Official" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown official\"]"));

        await this._sut.AdminDeleteAsync("admin1", 1);

        this._kojiService.Verify(k => k.RemoveGeofenceFromProjectAsync("Downtown Official"), Times.Once);
    }

    [Fact]
    public async Task AdminDeleteAsyncDoesNotCallKojiWhenNotApproved()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown", Status = "active" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\"]"));

        await this._sut.AdminDeleteAsync("admin1", 1);

        this._kojiService.Verify(k => k.RemoveGeofenceFromProjectAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AdminDeleteAsyncThrowsWhenNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this._sut.AdminDeleteAsync("admin1", 99));
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAllAsyncDelegatesToRepository()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1" },
            new() { Id = 2, KojiName = "area2" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetAllAsync();

        Assert.Equal(geofences, result);
        this._repository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    // --- GetAllWithDetailsAsync ---

    [Fact]
    public async Task GetAllWithDetailsAsyncReturnsEnrichedGeofencesWithOwnerNames()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", PolygonJson = "[[1,2],[3,4],[5,6]]" },
            new() { Id = 2, KojiName = "area2", HumanId = "user2", PolygonJson = "[[7,8],[9,10],[11,12]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" },
            new() { Id = "user2", Name = "Bob" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0].OwnerName);
        Assert.Equal("Bob", result[1].OwnerName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncFallsBackToHumanIdWhenHumanNotFound()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "known_user", PolygonJson = "[[1,2],[3,4],[5,6]]" },
            new() { Id = 2, KojiName = "area2", HumanId = "unknown_user", PolygonJson = "[[7,8],[9,10],[11,12]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "known_user", Name = "Alice" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal("Alice", result[0].OwnerName);
        Assert.Equal("unknown_user", result[1].OwnerName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncFallsBackToHumanIdWhenNameIsNull()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = null }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal("user1", result[0].OwnerName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncParsesPolygonJsonAndSetsPointCount()
    {
        var polygon = new[] { new[] { 1.0, 2.0 }, [3.0, 4.0], [5.0, 6.0], [7.0, 8.0] };
        var polygonJson = System.Text.Json.JsonSerializer.Serialize(polygon);
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "u1", PolygonJson = polygonJson }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<Human> { new() { Id = "u1", Name = "User" } }.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.NotNull(result[0].Polygon);
        Assert.Equal(4, result[0].Polygon!.Length);
        Assert.Equal(4, result[0].PointCount);
        Assert.Equal(1.0, result[0].Polygon![0][0]);
        Assert.Equal(2.0, result[0].Polygon![0][1]);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncHandlesNullPolygonJsonGracefully()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "u1", PolygonJson = null }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<Human> { new() { Id = "u1", Name = "User" } }.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Null(result[0].Polygon);
        Assert.Equal(0, result[0].PointCount);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncHandlesEmptyPolygonJsonGracefully()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "u1", PolygonJson = "" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<Human> { new() { Id = "u1", Name = "User" } }.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Null(result[0].Polygon);
        Assert.Equal(0, result[0].PointCount);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncReturnsEmptyListWithoutErrors()
    {
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Empty(result);
        this._humanRepo.Verify(r => r.GetByIdsAsync(It.Is<IEnumerable<string>>(ids => !ids.Any())), Times.Once);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncDeduplicatesHumanIdLookups()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "u1" },
            new() { Id = 2, KojiName = "area2", HumanId = "u1" },
            new() { Id = 3, KojiName = "area3", HumanId = "u2" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<Human>
            {
                new() { Id = "u1", Name = "Alice" },
                new() { Id = "u2", Name = "Bob" }
            }.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        // Verify GetByIdsAsync was called with only distinct IDs (2, not 3)
        this._humanRepo.Verify(r => r.GetByIdsAsync(It.Is<IEnumerable<string>>(
            ids => ids.Count() == 2)), Times.Once);
        // Both geofences from u1 should have the same owner name
        Assert.Equal("Alice", result[0].OwnerName);
        Assert.Equal("Alice", result[1].OwnerName);
        Assert.Equal("Bob", result[2].OwnerName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncResolvesReviewedByToName()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", ReviewedBy = "admin1", PolygonJson = "[[1,2],[3,4],[5,6]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" },
            new() { Id = "admin1", Name = "AdminUser" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal("AdminUser", result[0].ReviewedByName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncLeavesReviewedByNameNullWhenReviewedByIsNull()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", ReviewedBy = null, PolygonJson = "[[1,2],[3,4],[5,6]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Null(result[0].ReviewedByName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncFallsBackToReviewedByIdWhenReviewerNotFound()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", ReviewedBy = "unknown_admin", PolygonJson = "[[1,2],[3,4],[5,6]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal("unknown_admin", result[0].ReviewedByName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncFallsBackToReviewedByIdWhenReviewerNameIsNull()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", ReviewedBy = "admin1", PolygonJson = "[[1,2],[3,4],[5,6]]" }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" },
            new() { Id = "admin1", Name = null }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        Assert.Equal("admin1", result[0].ReviewedByName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncBatchesFetchesOwnerAndReviewerIdsInSingleCall()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "user1", ReviewedBy = "admin1" },
            new() { Id = 2, KojiName = "area2", HumanId = "user2", ReviewedBy = "admin1" },
            new() { Id = 3, KojiName = "area3", HumanId = "user1", ReviewedBy = null }
        };
        var humans = new List<Human>
        {
            new() { Id = "user1", Name = "Alice" },
            new() { Id = "user2", Name = "Bob" },
            new() { Id = "admin1", Name = "AdminUser" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        // Verify single batch call with 3 distinct IDs (user1, user2, admin1)
        this._humanRepo.Verify(r => r.GetByIdsAsync(It.Is<IEnumerable<string>>(
            ids => ids.Count() == 3)), Times.Once);
        Assert.Equal("AdminUser", result[0].ReviewedByName);
        Assert.Equal("AdminUser", result[1].ReviewedByName);
        Assert.Null(result[2].ReviewedByName);
    }

    [Fact]
    public async Task GetAllWithDetailsAsyncHandlesReviewerWhoIsAlsoOwner()
    {
        // When the reviewer is the same person as the owner, only one lookup should be made
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "admin1", ReviewedBy = "admin1" }
        };
        var humans = new List<Human>
        {
            new() { Id = "admin1", Name = "SelfReviewer" }
        };
        this._repository.Setup(r => r.GetAllAsync()).ReturnsAsync(geofences);
        this._humanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(humans.AsEnumerable());

        var result = await this._sut.GetAllWithDetailsAsync();

        // Verify single batch call with 1 distinct ID (admin1 is both owner and reviewer)
        this._humanRepo.Verify(r => r.GetByIdsAsync(It.Is<IEnumerable<string>>(
            ids => ids.Count() == 1)), Times.Once);
        Assert.Equal("SelfReviewer", result[0].OwnerName);
        Assert.Equal("SelfReviewer", result[0].ReviewedByName);
    }

    // --- GetPendingSubmissionsAsync ---

    [Fact]
    public async Task GetPendingSubmissionsAsyncDelegatesToRepository()
    {
        var submissions = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "pending1", Status = "pending_review" }
        };
        this._repository.Setup(r => r.GetByStatusAsync("pending_review")).ReturnsAsync(submissions);

        var result = await this._sut.GetPendingSubmissionsAsync();

        Assert.Equal(submissions, result);
        this._repository.Verify(r => r.GetByStatusAsync("pending_review"), Times.Once);
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

    // --- AddToProfileAsync ---

    [Fact]
    public async Task AddToProfileAsyncAddsAreaNameViaProxy()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"existing\"]"));

        await this._sut.AddToProfileAsync("u1", 1, 1);

        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            a.Contains("downtown") && a.Contains("existing"))), Times.Once);
    }

    [Fact]
    public async Task AddToProfileAsyncThrowsWhenGeofenceNotFound()
    {
        this._repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((UserGeofence?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.AddToProfileAsync("u1", 1, 99));
    }

    [Fact]
    public async Task AddToProfileAsyncThrowsWhenNotOwned()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "other_user", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => this._sut.AddToProfileAsync("u1", 1, 1));
    }

    [Fact]
    public async Task AddToProfileAsyncIsIdempotent()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"downtown\"]"));

        await this._sut.AddToProfileAsync("u1", 1, 1);

        // Already contains "downtown", so SetAreasAsync should not be called
        this._humanProxy.Verify(p => p.SetAreasAsync(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
    }

    // --- RemoveFromProfileAsync ---

    [Fact]
    public async Task RemoveFromProfileAsyncRemovesAreaNameViaProxy()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "u1", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);
        this._humanProxy.Setup(p => p.GetHumanAsync("u1")).ReturnsAsync(MakeHumanJson("u1", "[\"existing\",\"downtown\"]"));

        await this._sut.RemoveFromProfileAsync("u1", 1, 1);

        this._humanProxy.Verify(p => p.SetAreasAsync("u1", It.Is<string[]>(a =>
            !a.Contains("downtown") && a.Contains("existing"))), Times.Once);
    }

    [Fact]
    public async Task RemoveFromProfileAsyncThrowsWhenNotOwned()
    {
        var geofence = new UserGeofence { Id = 1, HumanId = "other_user", KojiName = "downtown" };
        this._repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(geofence);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => this._sut.RemoveFromProfileAsync("u1", 1, 1));
    }
}
