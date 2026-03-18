using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class RaidControllerTests : ControllerTestBase
{
    private readonly Mock<IRaidService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly RaidController _sut;

    public RaidControllerTests()
    {
        _sut = new RaidController(_service.Object, _mapper.Object);
        SetupUser(_sut);
    }

    [Fact]
    public async Task GetAll_ReturnsOk()
    {
        _service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(new List<Raid> { new() { Uid = 1 } });
        var result = await _sut.GetAll();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetByUid_ReturnsOk_WhenFound()
    {
        _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Raid { Uid = 1 });
        Assert.IsType<OkObjectResult>(await _sut.GetByUid(1));
    }

    [Fact]
    public async Task GetByUid_ReturnsNotFound()
    {
        _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.IsType<NotFoundResult>(await _sut.GetByUid(999));
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var model = new RaidCreate();
        var raid = new Raid { Uid = 1 };
        _mapper.Setup(m => m.Map<Raid>(model)).Returns(raid);
        _service.Setup(s => s.CreateAsync("123456789", raid)).ReturnsAsync(raid);
        Assert.IsType<CreatedAtActionResult>(await _sut.Create(model));
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenFound()
    {
        var existing = new Raid { Uid = 1 };
        _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(existing);
        _service.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await _sut.Update(1, new RaidUpdate()));
    }

    [Fact]
    public async Task Update_ReturnsNotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Raid?)null); Assert.IsType<NotFoundResult>(await _sut.Update(999, new RaidUpdate())); }

    [Fact]
    public async Task Delete_ReturnsNoContent() { _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true); Assert.IsType<NoContentResult>(await _sut.Delete(1)); }

    [Fact]
    public async Task Delete_ReturnsNotFound() { _service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false); Assert.IsType<NotFoundResult>(await _sut.Delete(999)); }

    [Fact]
    public async Task DeleteAll_ReturnsOk() { _service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(3); Assert.IsType<OkObjectResult>(await _sut.DeleteAll()); }

    [Fact]
    public async Task UpdateAllDistance_ReturnsOk() { _service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 100)).ReturnsAsync(2); Assert.IsType<OkObjectResult>(await _sut.UpdateAllDistance(100)); }
}
