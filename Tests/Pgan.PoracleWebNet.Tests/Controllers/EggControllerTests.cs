using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class EggControllerTests : ControllerTestBase
{
    private readonly Mock<IEggService> _service = new();
    private readonly EggController _sut;

    public EggControllerTests()
    {
        this._sut = new EggController(this._service.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllReturnsOk()
    {
        this._service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync([]);
        Assert.IsType<OkObjectResult>(await this._sut.GetAll());
    }
    [Fact]
    public async Task GetByUidReturnsOk()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Egg { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }
    [Fact]
    public async Task GetByUidNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Egg?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var model = new EggCreate();
        var egg = new Egg { Uid = 1 };
        this._service.Setup(s => s.CreateAsync("123456789", It.IsAny<Egg>())).ReturnsAsync(egg);
        var result = await this._sut.Create(model);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(EggController.GetByUid), created.ActionName);
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Egg { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync("123456789", existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new EggUpdate()));
    }

    [Fact]
    public async Task UpdateNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Egg?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new EggUpdate()));
    }
    [Fact]
    public async Task DeleteNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Egg { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }
    [Fact]
    public async Task DeleteNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Egg?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
    }
    [Fact]
    public async Task DeleteAllReturnsOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(4);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }
    [Fact]
    public async Task UpdateAllDistanceReturnsOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 200)).ReturnsAsync(2);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(200));
    }
}
