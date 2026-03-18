using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class NestControllerTests : ControllerTestBase
{
    private readonly Mock<INestService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly NestController _sut;

    public NestControllerTests() { _sut = new NestController(_service.Object, _mapper.Object); SetupUser(_sut); }

    [Fact] public async Task GetAll_Ok() { _service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(new List<Nest>()); Assert.IsType<OkObjectResult>(await _sut.GetAll()); }
    [Fact] public async Task GetByUid_Ok() { _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Nest { Uid = 1 }); Assert.IsType<OkObjectResult>(await _sut.GetByUid(1)); }
    [Fact] public async Task GetByUid_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Nest?)null); Assert.IsType<NotFoundResult>(await _sut.GetByUid(999)); }
    [Fact] public async Task Create_Created() { var m = new NestCreate(); var n = new Nest { Uid = 1 }; _mapper.Setup(x => x.Map<Nest>(m)).Returns(n); _service.Setup(s => s.CreateAsync("123456789", n)).ReturnsAsync(n); Assert.IsType<CreatedAtActionResult>(await _sut.Create(m)); }
    [Fact] public async Task Update_Ok() { var e = new Nest { Uid = 1 }; _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(e); _service.Setup(s => s.UpdateAsync(e)).ReturnsAsync(e); Assert.IsType<OkObjectResult>(await _sut.Update(1, new NestUpdate())); }
    [Fact] public async Task Update_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Nest?)null); Assert.IsType<NotFoundResult>(await _sut.Update(999, new NestUpdate())); }
    [Fact] public async Task Delete_NoContent() { _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true); Assert.IsType<NoContentResult>(await _sut.Delete(1)); }
    [Fact] public async Task Delete_NotFound() { _service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false); Assert.IsType<NotFoundResult>(await _sut.Delete(999)); }
    [Fact] public async Task DeleteAll_Ok() { _service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(6); Assert.IsType<OkObjectResult>(await _sut.DeleteAll()); }
    [Fact] public async Task UpdateAllDistance_Ok() { _service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 150)).ReturnsAsync(4); Assert.IsType<OkObjectResult>(await _sut.UpdateAllDistance(150)); }
}
