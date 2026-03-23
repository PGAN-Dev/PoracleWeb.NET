using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class GymControllerTests : ControllerTestBase
{
    private readonly Mock<IGymService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly GymController _sut;

    public GymControllerTests()
    {
        this._sut = new GymController(this._service.Object, this._mapper.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllOk()
    {
        this._service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync([]);
        Assert.IsType<OkObjectResult>(await this._sut.GetAll());
    }
    [Fact]
    public async Task GetByUidOk()
    {
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Gym { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }
    [Fact]
    public async Task GetByUidNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Gym?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }
    [Fact]
    public async Task CreateCreated()
    {
        var m = new GymCreate();
        var g = new Gym { Uid = 1 };
        this._mapper.Setup(x => x.Map<Gym>(m)).Returns(g);
        this._service.Setup(s => s.CreateAsync("123456789", g)).ReturnsAsync(g);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(m));
    }
    [Fact]
    public async Task UpdateOk()
    {
        var e = new Gym { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(e);
        this._service.Setup(s => s.UpdateAsync(e)).ReturnsAsync(e);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new GymUpdate()));
    }
    [Fact]
    public async Task UpdateNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Gym?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new GymUpdate()));
    }
    [Fact]
    public async Task DeleteNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Gym { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }
    [Fact]
    public async Task DeleteNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Gym?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
    }
    [Fact]
    public async Task DeleteAllOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(7);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }
    [Fact]
    public async Task UpdateAllDistanceOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 250)).ReturnsAsync(5);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(250));
    }
}
