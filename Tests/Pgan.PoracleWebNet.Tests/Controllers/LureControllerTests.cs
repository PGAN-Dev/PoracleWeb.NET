using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class LureControllerTests : ControllerTestBase
{
    private readonly Mock<ILureService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly LureController _sut;

    public LureControllerTests()
    {
        this._sut = new LureController(this._service.Object, this._mapper.Object);
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
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Lure { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }
    [Fact]
    public async Task GetByUidNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Lure?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }
    [Fact]
    public async Task CreateCreated()
    {
        var m = new LureCreate();
        var l = new Lure { Uid = 1 };
        this._mapper.Setup(x => x.Map<Lure>(m)).Returns(l);
        this._service.Setup(s => s.CreateAsync("123456789", l)).ReturnsAsync(l);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(m));
    }
    [Fact]
    public async Task UpdateOk()
    {
        var e = new Lure { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(e);
        this._service.Setup(s => s.UpdateAsync("123456789", e)).ReturnsAsync(e);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new LureUpdate()));
    }
    [Fact]
    public async Task UpdateNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Lure?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new LureUpdate()));
    }
    [Fact]
    public async Task DeleteNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Lure { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }
    [Fact]
    public async Task DeleteNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Lure?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
    }
    [Fact]
    public async Task DeleteAllOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(5);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }
    [Fact]
    public async Task UpdateAllDistanceOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 300)).ReturnsAsync(3);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(300));
    }
}
