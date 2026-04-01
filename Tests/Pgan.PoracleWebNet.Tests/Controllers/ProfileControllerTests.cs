using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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

    public ProfileControllerTests()
    {
        var jwtSettings = Options.Create(new JwtSettings { Secret = "test-secret-key-that-is-long-enough", Issuer = "test", Audience = "test" });
        this._sut = new ProfileController(this._profileService.Object, this._humanService.Object, this._humanProxy.Object, jwtSettings);
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
        this._humanProxy.Verify(p => p.AddProfileAsync("123456789", It.IsAny<System.Text.Json.JsonElement>()), Times.Once);
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
        this._humanProxy.Verify(p => p.UpdateProfileAsync("123456789", It.IsAny<System.Text.Json.JsonElement>()), Times.Once);
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
}
