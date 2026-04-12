using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class MaxBattleControllerTests : ControllerTestBase
{
    private readonly Mock<IMaxBattleService> _service = new();
    private readonly MaxBattleController _sut;

    public MaxBattleControllerTests()
    {
        this._sut = new MaxBattleController(this._service.Object);
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
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new MaxBattle { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }

    [Fact]
    public async Task GetByUidReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((MaxBattle?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var model = new MaxBattleCreate();
        var maxBattle = new MaxBattle { Uid = 1 };
        this._service.Setup(s => s.CreateAsync("123456789", It.IsAny<MaxBattle>())).ReturnsAsync(maxBattle);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(model));
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new MaxBattle { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync("123456789", existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new MaxBattleUpdate()));
    }

    [Fact]
    public async Task UpdateReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((MaxBattle?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new MaxBattleUpdate()));
    }

    [Fact]
    public async Task DeleteReturnsNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new MaxBattle { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }

    [Fact]
    public async Task DeleteReturnsNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((MaxBattle?)null);
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

    [Fact]
    public async Task UpdateBulkDistanceReturnsOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), "123456789", 50)).ReturnsAsync(2);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateBulkDistance(new BulkDistanceRequest { Uids = [1, 2], Distance = 50 }));
    }
}
