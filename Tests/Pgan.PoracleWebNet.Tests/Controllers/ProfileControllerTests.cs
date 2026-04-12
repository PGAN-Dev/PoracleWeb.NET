using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ProfileControllerTests : ControllerTestBase
{
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly ProfileController _sut;

    private readonly Mock<IJwtService> _jwtService = new();

    public ProfileControllerTests()
    {
        this._jwtService.Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<int>()))
            .Returns("test-jwt-token");
        this._sut = new ProfileController(this._profileService.Object, this._humanService.Object, this._humanProxy.Object, this._jwtService.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllReturnsOkWithProfiles()
    {
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([new() { ProfileNo = 1 }]);
        var result = await this._sut.GetAll();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var profile = new Profile { Name = "New" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);
        var result = await this._sut.Create(profile);
        Assert.IsType<CreatedAtActionResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task CreateSetsUserId()
    {
        var profile = new Profile { Name = "New" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);
        await this._sut.Create(profile);
        Assert.Equal("123456789", profile.Id);
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);
        var result = await this._sut.Update(1, new Profile { Name = "Updated" });
        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.UpdateProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task UpdateReturnsNotFoundWhenMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(99, new Profile()));
    }

    [Fact]
    public async Task SwitchProfileReturnsOkAndCallsProxy()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2, Area = "[\"new area\"]" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);

        var result = await this._sut.SwitchProfile(2);

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.SwitchProfileAsync("123456789", 2), Times.Once);
    }

    [Fact]
    public async Task SwitchProfileReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.SwitchProfile(99));
    }

    [Fact]
    public async Task DuplicateCreatesProfileAndCopiesAlarms()
    {
        var sourceProfile = new Profile { Id = "123456789", ProfileNo = 1, Name = "Main", Area = "[\"area1\"]" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(sourceProfile);
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([sourceProfile]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(new Profile { ProfileNo = 2, Name = "Main (copy)" });

        var result = await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Main (copy)" });

        Assert.IsType<CreatedAtActionResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
        this._profileService.Verify(s => s.CopyAsync("123456789", 1, 2), Times.Once);
    }

    [Fact]
    public async Task DuplicateCopiesActiveHoursFromSource()
    {
        var schedule = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var sourceProfile = new Profile { Id = "123456789", ProfileNo = 1, Name = "Main", ActiveHours = schedule };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(sourceProfile);
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([sourceProfile]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(new Profile { ProfileNo = 2, Name = "Copy" });

        JsonElement? capturedBody = null;
        this._humanProxy
            .Setup(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" });

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(schedule, ah.GetString());
    }

    [Fact]
    public async Task DuplicateReturnsNotFoundWhenSourceMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 99, Name = "Copy" }));
    }

    [Fact]
    public async Task DeleteReturnsNoContentAndCallsProxy()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 2 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(existing);

        Assert.IsType<NoContentResult>(await this._sut.Delete(2));
        this._humanProxy.Verify(p => p.DeleteProfileAsync("123456789", 2), Times.Once);
    }

    [Fact]
    public async Task DeleteReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(99));
    }

    [Fact]
    public async Task UpdateIncludesActiveHoursInProxyPayload()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

        var profile = new Profile { Name = "Updated", ActiveHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]" };
        await this._sut.Update(1, profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(/*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]", ah.GetString());
    }

    [Fact]
    public async Task UpdateSendsNullActiveHoursWhenNotProvided()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

        var profile = new Profile { Name = "Updated", ActiveHours = null };
        await this._sut.Update(1, profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(JsonValueKind.Null, ah.ValueKind);
    }

    [Fact]
    public async Task CreateIncludesActiveHoursInProxyPayload()
    {
        var activeHours = /*lang=json,strict*/ "[{\"day\":2,\"hours\":\"18\",\"mins\":\"30\"}]";
        var profile = new Profile { Name = "New", ActiveHours = activeHours };
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

        await this._sut.Create(profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(activeHours, ah.GetString());
    }

    [Fact]
    public async Task GetAllReturnsProfilesWithActiveHours()
    {
        var activeHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var profiles = new List<Profile>
        {
            new() { ProfileNo = 1, Name = "Default", ActiveHours = activeHours },
            new() { ProfileNo = 2, Name = "PvP", ActiveHours = null },
        };
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync(profiles);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(new Human { CurrentProfileNo = 1 });

        var result = await this._sut.GetAll();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedProfiles = Assert.IsType<List<Profile>>(okResult.Value, exactMatch: false);

        Assert.Equal(2, returnedProfiles.Count);
        Assert.Equal(activeHours, returnedProfiles[0].ActiveHours);
        Assert.Null(returnedProfiles[1].ActiveHours);
    }
}
