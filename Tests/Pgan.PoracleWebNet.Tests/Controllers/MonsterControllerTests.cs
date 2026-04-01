using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class MonsterControllerTests : ControllerTestBase
{
    private readonly Mock<IMonsterService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly MonsterController _sut;

    public MonsterControllerTests()
    {
        this._sut = new MonsterController(this._service.Object, this._mapper.Object);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllReturnsOkWithMonsters()
    {
        var monsters = new List<Monster> { new() { Uid = 1, PokemonId = 25 } };
        this._service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(monsters);

        var result = await this._sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(monsters, ok.Value);
    }

    [Fact]
    public async Task GetByUidReturnsOkWhenFound()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(monster);

        var result = await this._sut.GetByUid(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(monster, ok.Value);
    }

    [Fact]
    public async Task GetByUidReturnsNotFoundWhenMissing()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Monster?)null);

        var result = await this._sut.GetByUid(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var createModel = new MonsterCreate();
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        this._mapper.Setup(m => m.Map<Monster>(createModel)).Returns(monster);
        this._service.Setup(s => s.CreateAsync("123456789", monster)).ReturnsAsync(monster);

        var result = await this._sut.Create(createModel);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(MonsterController.GetByUid), created.ActionName);
        Assert.Equal(monster, created.Value);
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Monster { Uid = 1, PokemonId = 25, Id = "123456789" };
        var updateModel = new MonsterUpdate();
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync("123456789", existing)).ReturnsAsync(existing);

        var result = await this._sut.Update(1, updateModel);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateReturnsNotFoundWhenMissing()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Monster?)null);

        var result = await this._sut.Update(999, new MonsterUpdate());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteReturnsNoContentWhenDeleted()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Monster { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 1)).ReturnsAsync(true);

        var result = await this._sut.Delete(1);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteReturnsNotFoundWhenMissing()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Monster?)null);

        var result = await this._sut.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteAllReturnsOkWithCount()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(5);

        var result = await this._sut.DeleteAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task UpdateAllDistanceReturnsOkWithCount()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 500)).ReturnsAsync(3);

        var result = await this._sut.UpdateAllDistance(500);

        Assert.IsType<OkObjectResult>(result);
    }
}
