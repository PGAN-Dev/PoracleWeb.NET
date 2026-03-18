using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class ProfileControllerTests : ControllerTestBase
{
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly ProfileController _sut;

    public ProfileControllerTests()
    {
        _sut = new ProfileController(_profileService.Object, _humanService.Object);
        SetupUser(_sut);
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithProfiles()
    {
        _profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync(new List<Profile> { new() { ProfileNo = 1 } });
        var result = await _sut.GetAll();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var profile = new Profile { Name = "New" };
        _profileService.Setup(s => s.CreateAsync(It.IsAny<Profile>())).ReturnsAsync(profile);
        var result = await _sut.Create(profile);
        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_SetsUserId()
    {
        var profile = new Profile { Name = "New" };
        _profileService.Setup(s => s.CreateAsync(It.Is<Profile>(p => p.Id == "123456789"))).ReturnsAsync(profile);
        await _sut.Create(profile);
        Assert.Equal("123456789", profile.Id);
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenFound()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        _profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);
        _profileService.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await _sut.Update(1, new Profile { Name = "Updated" }));
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMissing()
    {
        _profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await _sut.Update(99, new Profile()));
    }

    [Fact]
    public async Task SwitchProfile_ReturnsOk_WhenBothExist()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2 };
        var human = new Human { Id = "123456789", CurrentProfileNo = 1 };
        _profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(human);
        _humanService.Setup(s => s.UpdateAsync(human)).ReturnsAsync(human);

        var result = await _sut.SwitchProfile(2);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, human.CurrentProfileNo);
    }

    [Fact]
    public async Task SwitchProfile_ReturnsNotFound_WhenProfileMissing()
    {
        _profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await _sut.SwitchProfile(99));
    }

    [Fact]
    public async Task SwitchProfile_ReturnsNotFound_WhenHumanMissing()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2 };
        _profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);
        _humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync((Human?)null);
        Assert.IsType<NotFoundResult>(await _sut.SwitchProfile(2));
    }

    [Fact]
    public async Task Delete_ReturnsNoContent() { _profileService.Setup(s => s.DeleteAsync("123456789", 2)).ReturnsAsync(true); Assert.IsType<NoContentResult>(await _sut.Delete(2)); }

    [Fact]
    public async Task Delete_ReturnsNotFound() { _profileService.Setup(s => s.DeleteAsync("123456789", 99)).ReturnsAsync(false); Assert.IsType<NotFoundResult>(await _sut.Delete(99)); }
}
