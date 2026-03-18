using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PGAN.Poracle.Web.Api.Controllers;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Tests.Controllers;

public class QuestControllerTests : ControllerTestBase
{
    private readonly Mock<IQuestService> _service = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly QuestController _sut;

    public QuestControllerTests() { _sut = new QuestController(_service.Object, _mapper.Object); SetupUser(_sut); }

    [Fact] public async Task GetAll_ReturnsOk() { _service.Setup(s => s.GetByUserAsync("123456789", 1)).ReturnsAsync(new List<Quest>()); Assert.IsType<OkObjectResult>(await _sut.GetAll()); }
    [Fact] public async Task GetByUid_ReturnsOk() { _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Quest { Uid = 1 }); Assert.IsType<OkObjectResult>(await _sut.GetByUid(1)); }
    [Fact] public async Task GetByUid_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Quest?)null); Assert.IsType<NotFoundResult>(await _sut.GetByUid(999)); }
    [Fact] public async Task Create_ReturnsCreated() { var m = new QuestCreate(); var q = new Quest { Uid = 1 }; _mapper.Setup(x => x.Map<Quest>(m)).Returns(q); _service.Setup(s => s.CreateAsync("123456789", q)).ReturnsAsync(q); Assert.IsType<CreatedAtActionResult>(await _sut.Create(m)); }
    [Fact] public async Task Update_ReturnsOk() { var e = new Quest { Uid = 1 }; _service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(e); _service.Setup(s => s.UpdateAsync(e)).ReturnsAsync(e); Assert.IsType<OkObjectResult>(await _sut.Update(1, new QuestUpdate())); }
    [Fact] public async Task Update_NotFound() { _service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Quest?)null); Assert.IsType<NotFoundResult>(await _sut.Update(999, new QuestUpdate())); }
    [Fact] public async Task Delete_NoContent() { _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true); Assert.IsType<NoContentResult>(await _sut.Delete(1)); }
    [Fact] public async Task Delete_NotFound() { _service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false); Assert.IsType<NotFoundResult>(await _sut.Delete(999)); }
    [Fact] public async Task DeleteAll_Ok() { _service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(2); Assert.IsType<OkObjectResult>(await _sut.DeleteAll()); }
    [Fact] public async Task UpdateAllDistance_Ok() { _service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 50)).ReturnsAsync(1); Assert.IsType<OkObjectResult>(await _sut.UpdateAllDistance(50)); }
}
