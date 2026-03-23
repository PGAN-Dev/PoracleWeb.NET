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
    private readonly ProfileController _sut;

    public ProfileControllerTests()
    {
        var jwtSettings = Options.Create(new JwtSettings { Secret = "test-secret-key-that-is-long-enough", Issuer = "test", Audience = "test" });
        this._sut = new ProfileController(this._profileService.Object, this._humanService.Object, jwtSettings);
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
        this._profileService.Setup(s => s.CreateAsync(It.IsAny<Profile>())).ReturnsAsync(profile);
        var result = await this._sut.Create(profile);
        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task CreateSetsUserId()
    {
        var profile = new Profile { Name = "New" };
        this._profileService.Setup(s => s.CreateAsync(It.Is<Profile>(p => p.Id == "123456789"))).ReturnsAsync(profile);
        await this._sut.Create(profile);
        Assert.Equal("123456789", profile.Id);
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);
        this._profileService.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new Profile { Name = "Updated" }));
    }

    [Fact]
    public async Task UpdateReturnsNotFoundWhenMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(99, new Profile()));
    }

    [Fact]
    public async Task SwitchProfileReturnsOkWhenBothExist()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2 };
        var human = new Human { Id = "123456789", CurrentProfileNo = 1 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await this._sut.SwitchProfile(2);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, human.CurrentProfileNo);
    }

    [Fact]
    public async Task SwitchProfileSyncsLocationToHumans()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2, Latitude = 40.7128, Longitude = -74.006 };
        var human = new Human { Id = "123456789", CurrentProfileNo = 1, Latitude = 0, Longitude = 0 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        this._humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        await this._sut.SwitchProfile(2);

        Assert.Equal(40.7128, human.Latitude);
        Assert.Equal(-74.006, human.Longitude);
    }

    [Fact]
    public async Task SwitchProfileReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.SwitchProfile(99));
    }

    [Fact]
    public async Task SwitchProfileReturnsNotFoundWhenHumanMissing()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await this._sut.SwitchProfile(2));
    }

    [Fact]
    public async Task DeleteReturnsNoContent()
    {
        this._profileService.Setup(s => s.DeleteAsync("123456789", 2)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(2));
    }

    [Fact]
    public async Task DeleteReturnsNotFound()
    {
        this._profileService.Setup(s => s.DeleteAsync("123456789", 99)).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(99));
    }
}
