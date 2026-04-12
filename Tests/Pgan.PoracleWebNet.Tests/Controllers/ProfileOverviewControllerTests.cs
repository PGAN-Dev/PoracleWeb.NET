using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ProfileOverviewControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IProfileOverviewService> _service = new();
    private readonly ProfileOverviewController _sut;

    private readonly Mock<IJwtService> _jwtService = new();

    public ProfileOverviewControllerTests()
    {
        this._jwtService.Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<int>()))
            .Returns("test-jwt-token");
        this._sut = new ProfileOverviewController(
            this._service.Object,
            this._profileService.Object,
            this._humanProxy.Object,
            this._jwtService.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllProfilesOverviewCallsServiceWithUserId()
    {
        var json = CreateJsonObject(new
        {
        });
        this._service.Setup(s => s.GetAllProfilesOverviewAsync("123456789")).ReturnsAsync(json);

        await this._sut.GetAllProfilesOverview();

        this._service.Verify(s => s.GetAllProfilesOverviewAsync("123456789"), Times.Once);
    }

    [Fact]
    public async Task GetAllProfilesOverviewReturnsOkWithData()
    {
        var json = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 1 } }
        });
        this._service.Setup(s => s.GetAllProfilesOverviewAsync("123456789")).ReturnsAsync(json);

        var result = await this._sut.GetAllProfilesOverview();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(json.ToString(), ok.Value?.ToString());
    }

    [Fact]
    public async Task DuplicateProfileReturnsNotFoundWhenSourceMissing()
    {
        this._profileService
            .Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99))
            .ReturnsAsync((Core.Models.Profile?)null);

        var result = await this._sut.DuplicateProfile(99, new ProfileOverviewDuplicateRequest("Copy"));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DuplicateProfileReturnsOkWithAlarmCount()
    {
        var source = new Core.Models.Profile { ProfileNo = 1, Name = "Main", Area = "[]" };
        this._profileService
            .Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1))
            .ReturnsAsync(source);
        this._profileService
            .Setup(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([source]);
        this._humanProxy
            .Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Returns(Task.CompletedTask);
        this._service
            .Setup(s => s.DuplicateProfileAsync("123456789", 1, 2))
            .ReturnsAsync(5);

        var result = await this._sut.DuplicateProfile(1, new ProfileOverviewDuplicateRequest("Copy"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5, doc.RootElement.GetProperty("alarmsCopied").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("newProfileNo").GetInt32());
    }

    [Fact]
    public async Task ImportProfileReturnsOkWithAlarmCount()
    {
        this._profileService
            .Setup(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([new Core.Models.Profile { ProfileNo = 1, Name = "Main" }]);
        this._humanProxy
            .Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Returns(Task.CompletedTask);
        var alarms = CreateJsonObject(new
        {
            pokemon = new[] { new { pokemon_id = 1 } }
        });
        this._service
            .Setup(s => s.ImportAlarmsAsync("123456789", 2, It.IsAny<JsonElement>()))
            .ReturnsAsync(3);

        var request = new ProfileOverviewImportRequest("Imported", 1, alarms);
        var result = await this._sut.ImportProfile(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("alarmsCopied").GetInt32());
    }

    [Fact]
    public async Task ImportProfileDeduplicatesName()
    {
        var existing = new[]
        {
            new Core.Models.Profile { ProfileNo = 1, Name = "Work" },
            new Core.Models.Profile { ProfileNo = 2, Name = "Play" },
        };
        this._profileService
            .Setup(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync(existing);
        this._humanProxy
            .Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Returns(Task.CompletedTask);
        var alarms = CreateJsonObject(new
        {
            pokemon = new[] { new { pokemon_id = 1 } }
        });
        this._service
            .Setup(s => s.ImportAlarmsAsync("123456789", 3, It.IsAny<JsonElement>()))
            .ReturnsAsync(1);

        var request = new ProfileOverviewImportRequest("Work", 1, alarms);
        var result = await this._sut.ImportProfile(request);

        // Verify the profile was created with a deduplicated name (contains the body with "Work (2)")
        this._humanProxy.Verify(
            h => h.AddProfileAsync("123456789",
                It.Is<JsonElement>(j => j.GetProperty("name").GetString() == "Work (2)")),
            Times.Once);
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
