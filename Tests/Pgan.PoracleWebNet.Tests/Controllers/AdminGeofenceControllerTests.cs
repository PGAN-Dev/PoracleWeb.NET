using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AdminGeofenceControllerTests : ControllerTestBase
{
    private readonly Mock<IUserGeofenceService> _service = new();
    private readonly Mock<ILogger<AdminGeofenceController>> _logger = new();
    private readonly AdminGeofenceController _sut;

    public AdminGeofenceControllerTests()
    {
        this._sut = new AdminGeofenceController(this._service.Object, this._logger.Object);
        SetupUser(this._sut, isAdmin: true);
    }

    // --- GetAll (with details) ---

    [Fact]
    public async Task GetAllReturnsOkWithEnrichedList()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "downtown", DisplayName = "Downtown", Status = "active", OwnerName = "Alice", PointCount = 5 },
            new() { Id = 2, KojiName = "park", DisplayName = "Park", Status = "approved", OwnerName = "Bob", PointCount = 12 }
        };
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.Equal(2, list.Count);
        Assert.Equal("Alice", list[0].OwnerName);
        Assert.Equal("Bob", list[1].OwnerName);
    }

    [Fact]
    public async Task GetAllCallsGetAllWithDetailsAsyncNotGetAllAsync()
    {
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync([]);

        await this._sut.GetAll();

        this._service.Verify(s => s.GetAllWithDetailsAsync(), Times.Once);
        this._service.Verify(s => s.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task GetAllReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = await this._sut.GetAll();

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAllSetsReviewedByAvatarUrlWhenReviewedByIsPresent()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "111", ReviewedBy = "222", ReviewedByName = "AdminUser" }
        };
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.NotNull(list[0].ReviewedByAvatarUrl);
        Assert.Contains("discord", list[0].ReviewedByAvatarUrl!);
    }

    [Fact]
    public async Task GetAllDoesNotSetReviewedByAvatarUrlWhenReviewedByIsNull()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "111", ReviewedBy = null }
        };
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.Null(list[0].ReviewedByAvatarUrl);
    }

    [Fact]
    public async Task GetAllDoesNotSetReviewedByAvatarUrlWhenReviewedByIsEmpty()
    {
        var geofences = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "area1", HumanId = "111", ReviewedBy = "" }
        };
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync(geofences);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.Null(list[0].ReviewedByAvatarUrl);
    }

    [Fact]
    public async Task GetAllReturnsEmptyListWhenNoGeofences()
    {
        this._service.Setup(s => s.GetAllWithDetailsAsync()).ReturnsAsync([]);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserGeofence>>(ok.Value);
        Assert.Empty(list);
    }

    // --- AdminDelete ---

    [Fact]
    public async Task AdminDeleteReturnsNoContent()
    {
        this._service.Setup(s => s.AdminDeleteAsync("123456789", 1)).Returns(Task.CompletedTask);

        var result = await this._sut.AdminDelete(1);

        Assert.IsType<NoContentResult>(result);
        this._service.Verify(s => s.AdminDeleteAsync("123456789", 1), Times.Once);
    }

    [Fact]
    public async Task AdminDeleteReturnsNotFoundWhenNotExists()
    {
        this._service.Setup(s => s.AdminDeleteAsync("123456789", 99))
            .ThrowsAsync(new InvalidOperationException("Geofence with ID 99 not found."));

        var result = await this._sut.AdminDelete(99);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task AdminDeleteReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = await this._sut.AdminDelete(1);

        Assert.IsType<ForbidResult>(result);
    }

    // --- GetSubmissions ---

    [Fact]
    public async Task GetSubmissionsReturnsOkWithList()
    {
        var submissions = new List<UserGeofence>
        {
            new() { Id = 1, KojiName = "pweb_111_downtown", DisplayName = "Downtown", Status = "pending_review" },
            new() { Id = 2, KojiName = "pweb_222_park", DisplayName = "Park", Status = "pending_review" }
        };
        this._service.Setup(s => s.GetPendingSubmissionsAsync()).ReturnsAsync(submissions);

        var result = await this._sut.GetSubmissions();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(submissions, ok.Value);
    }

    [Fact]
    public async Task GetSubmissionsReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = await this._sut.GetSubmissions();

        Assert.IsType<ForbidResult>(result);
    }

    // --- ApproveSubmission ---

    [Fact]
    public async Task ApproveSubmissionReturnsOkWithApprovedGeofence()
    {
        var approved = new UserGeofence
        {
            Id = 1,
            KojiName = "pweb_111_downtown",
            DisplayName = "Downtown",
            Status = "approved",
            PromotedName = "Downtown Official"
        };
        this._service.Setup(s => s.ApproveSubmissionAsync("123456789", 1, "Downtown Official")).ReturnsAsync(approved);

        var result = await this._sut.ApproveSubmission(1, new AdminGeofenceController.ApproveRequest { PromotedName = "Downtown Official" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(approved, ok.Value);
    }

    [Fact]
    public async Task ApproveSubmissionReturnsNotFoundWhenNotFound()
    {
        this._service.Setup(s => s.ApproveSubmissionAsync("123456789", 99, null))
            .ThrowsAsync(new InvalidOperationException("Submission not found."));

        var result = await this._sut.ApproveSubmission(99, null);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task ApproveSubmissionReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = await this._sut.ApproveSubmission(1, null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ApproveSubmissionPassesNullPromotedNameWhenRequestIsNull()
    {
        var approved = new UserGeofence { Id = 1, Status = "approved" };
        this._service.Setup(s => s.ApproveSubmissionAsync("123456789", 1, null)).ReturnsAsync(approved);

        var result = await this._sut.ApproveSubmission(1, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        this._service.Verify(s => s.ApproveSubmissionAsync("123456789", 1, null), Times.Once);
    }

    // --- RejectSubmission ---

    [Fact]
    public async Task RejectSubmissionReturnsOkWithRejectedGeofence()
    {
        var rejected = new UserGeofence
        {
            Id = 1,
            KojiName = "pweb_111_downtown",
            DisplayName = "Downtown",
            Status = "rejected",
            ReviewNotes = "Area too large"
        };
        this._service.Setup(s => s.RejectSubmissionAsync("123456789", 1, "Area too large")).ReturnsAsync(rejected);

        var result = await this._sut.RejectSubmission(1, new AdminGeofenceController.RejectRequest { ReviewNotes = "Area too large" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<UserGeofence>(ok.Value);
        Assert.Equal("rejected", value.Status);
        Assert.Equal("Area too large", value.ReviewNotes);
    }

    [Fact]
    public async Task RejectSubmissionReturnsNotFoundWhenNotFound()
    {
        this._service.Setup(s => s.RejectSubmissionAsync("123456789", 99, "Not needed"))
            .ThrowsAsync(new InvalidOperationException("Submission not found."));

        var result = await this._sut.RejectSubmission(99, new AdminGeofenceController.RejectRequest { ReviewNotes = "Not needed" });

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task RejectSubmissionReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = await this._sut.RejectSubmission(1, new AdminGeofenceController.RejectRequest { ReviewNotes = "No" });

        Assert.IsType<ForbidResult>(result);
    }
}
