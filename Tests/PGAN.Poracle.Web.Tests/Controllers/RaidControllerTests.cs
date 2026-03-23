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
        this._sut = new RaidController(this._service.Object, this._mapper.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllReturnsOk()
    {
        this._service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync([new() { Uid = 1 }]);
        var result = await this._sut.GetAll();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetByUidReturnsOkWhenFound()
    {
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Raid { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }

    [Fact]
    public async Task GetByUidReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var model = new RaidCreate();
        var raid = new Raid { Uid = 1 };
        this._mapper.Setup(m => m.Map<Raid>(model)).Returns(raid);
        this._service.Setup(s => s.CreateAsync("123456789", raid)).ReturnsAsync(raid);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(model));
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Raid { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new RaidUpdate()));
    }

    [Fact]
    public async Task UpdateReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new RaidUpdate()));
    }

    [Fact]
    public async Task DeleteReturnsNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Raid { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }

    [Fact]
    public async Task DeleteReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Raid?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
    }

    [Fact]
    public async Task DeleteAllReturnsOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(3);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }

    [Fact]
    public async Task UpdateAllDistanceReturnsOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 100)).ReturnsAsync(2);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(100));
    }
}
