using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class EggControllerTests : ControllerTestBase
{
    private readonly Mock<IEggService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly EggController _sut;

    public EggControllerTests() { _sut = new EggController(_service.Object, _mapper.Object); SetupUser(_sut); }

    [Fact] public async Task GetAll_ReturnsOk() { _service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(new List<Egg>()); Assert.IsType<OkObjectResult>(await _sut.GetAll()); }
    [Fact] public async Task GetByUid_ReturnsOk() { _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Egg { Uid = 1 }); Assert.IsType<OkObjectResult>(await _sut.GetByUid(1)); }
    [Fact] public async Task GetByUid_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Egg?)null); Assert.IsType<NotFoundResult>(await _sut.GetByUid(999)); }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction()
    {
        var model = new EggCreate();
        var egg = new Egg { Uid = 1 };
        _mapper.Setup(m => m.Map<Egg>(model)).Returns(egg);
        _service.Setup(s => s.CreateAsync("123456789", egg)).ReturnsAsync(egg);
        var result = await _sut.Create(model);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(EggController.GetByUid), created.ActionName);
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenFound()
    {
        var existing = new Egg { Uid = 1 };
        _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(existing);
        _service.Setup(s => s.UpdateAsync(existing)).ReturnsAsync(existing);
        Assert.IsType<OkObjectResult>(await _sut.Update(1, new EggUpdate()));
        _mapper.Verify(m => m.Map(It.IsAny<EggUpdate>(), existing), Times.Once);
    }

    [Fact] public async Task Update_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Egg?)null); Assert.IsType<NotFoundResult>(await _sut.Update(999, new EggUpdate())); }
    [Fact] public async Task Delete_NoContent() { _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true); Assert.IsType<NoContentResult>(await _sut.Delete(1)); }
    [Fact] public async Task Delete_NotFound() { _service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false); Assert.IsType<NotFoundResult>(await _sut.Delete(999)); }
    [Fact] public async Task DeleteAll_ReturnsOk() { _service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(4); Assert.IsType<OkObjectResult>(await _sut.DeleteAll()); }
    [Fact] public async Task UpdateAllDistance_ReturnsOk() { _service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 200)).ReturnsAsync(2); Assert.IsType<OkObjectResult>(await _sut.UpdateAllDistance(200)); }
}
