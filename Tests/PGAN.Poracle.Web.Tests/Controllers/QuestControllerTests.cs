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

    public QuestControllerTests()
    {
        this._sut = new QuestController(this._service.Object, this._mapper.Object);
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
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(new Quest { Uid = 1 });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }
    [Fact]
    public async Task GetByUidNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Quest?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }
    [Fact]
    public async Task CreateReturnsCreated()
    {
        var m = new QuestCreate();
        var q = new Quest { Uid = 1 };
        this._mapper.Setup(x => x.Map<Quest>(m)).Returns(q);
        this._service.Setup(s => s.CreateAsync("123456789", q)).ReturnsAsync(q);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(m));
    }
    [Fact]
    public async Task UpdateReturnsOk()
    {
        var e = new Quest { Uid = 1 };
        this._service.Setup(s => s.GetByUidAsync(1)).ReturnsAsync(e);
        this._service.Setup(s => s.UpdateAsync(e)).ReturnsAsync(e);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new QuestUpdate()));
    }
    [Fact]
    public async Task UpdateNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync(999)).ReturnsAsync((Quest?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new QuestUpdate()));
    }
    [Fact]
    public async Task DeleteNoContent()
    {
        this._service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }
    [Fact]
    public async Task DeleteNotFound()
    {
        this._service.Setup(s => s.DeleteAsync(999)).ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
    }
    [Fact]
    public async Task DeleteAllOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(2);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }
    [Fact]
    public async Task UpdateAllDistanceOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 50)).ReturnsAsync(1);
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(50));
    }
}
