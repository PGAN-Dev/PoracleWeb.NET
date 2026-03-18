using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class MonsterControllerTests : ControllerTestBase
{
    private readonly Mock<IMonsterService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly MonsterController _sut;

    public MonsterControllerTests()
    {
        _sut = new MonsterController(_service.Object, _mapper.Object);
        SetupUser(_sut);
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithMonsters()
    {
        var monsters = new List<Monster> { new() { Uid = 1, PokemonId = 25 } };
        _service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(monsters);

        var result = await _sut.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(monsters, ok.Value);
    }

    [Fact]
    public async Task GetByUid_ReturnsOk_WhenFound()
    {
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(monster);

        var result = await _sut.GetByUid(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(monster, ok.Value);
    }

    [Fact]
    public async Task GetByUid_ReturnsNotFound_WhenMissing()
    {
        _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Monster?)null);

        var result = await _sut.GetByUid(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var createModel = new MonsterCreate();
        var monster = new Monster { Uid = 1, PokemonId = 25 };
        _mapper.Setup(m => m.Map<Monster>(createModel)).Returns(monster);
        _service.Setup(s => s.CreateAsync("123456789", monster)).ReturnsAsync(monster);

        var result = await _sut.Create(createModel);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(MonsterController.GetByUid), created.ActionName);
        Assert.Equal(monster, created.Value);
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenFound()
    {
        var existing = new Monster { Uid = 1, PokemonId = 25 };
        var updateModel = new MonsterUpdate();
        _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(existing);
        _service.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);

        var result = await _sut.Update(1, updateModel);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMissing()
    {
        _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Monster?)null);

        var result = await _sut.Update(999, new MonsterUpdate());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenDeleted()
    {
        _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true);

        var result = await _sut.Delete(1);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMissing()
    {
        _service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false);

        var result = await _sut.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteAll_ReturnsOkWithCount()
    {
        _service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(5);

        var result = await _sut.DeleteAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task UpdateAllDistance_ReturnsOkWithCount()
    {
        _service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 500)).ReturnsAsync(3);

        var result = await _sut.UpdateAllDistance(500);

        Assert.IsType<OkObjectResult>(result);
    }
}
