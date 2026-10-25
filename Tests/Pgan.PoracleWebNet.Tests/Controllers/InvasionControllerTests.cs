using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class InvasionControllerTests : ControllerTestBase
{
    private readonly Mock<IInvasionService> _service = new();
    private readonly InvasionController _sut;

    public InvasionControllerTests()
    {
        this._sut = new InvasionController(this._service.Object, UnlinkedRouter());
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
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Invasion { Uid = 1, Id = "123456789" });
        Assert.IsType<OkObjectResult>(await this._sut.GetByUid(1));
    }
    [Fact]
    public async Task GetByUidNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Invasion?)null);
        Assert.IsType<NotFoundResult>(await this._sut.GetByUid(999));
    }
    [Fact]
    public async Task CreateCreated()
    {
        var m = new InvasionCreate();
        var i = new Invasion { Uid = 1 };
        this._service.Setup(s => s.CreateAsync("123456789", It.IsAny<Invasion>())).ReturnsAsync(i);
        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(m));
    }
    [Fact]
    public async Task UpdateOk()
    {
        var e = new Invasion { Uid = 1, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(e);
        this._service.Setup(s => s.UpdateAsync("123456789", e)).ReturnsAsync(e);
        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new InvasionUpdate()));
    }
    [Fact]
    public async Task UpdateNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Invasion?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(999, new InvasionUpdate()));
    }
    [Fact]
    public async Task DeleteNoContent()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 1)).ReturnsAsync(new Invasion { Uid = 1, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 1)).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await this._sut.Delete(1));
    }
    [Fact]
    public async Task DeleteNotFound()
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 999)).ReturnsAsync((Invasion?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(999));
        this._service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
    [Fact]
    public async Task DeleteAllOk()
    {
        this._service.Setup(s => s.DeleteAllByUserAsync("123456789", 1)).ReturnsAsync(3);
        Assert.IsType<OkObjectResult>(await this._sut.DeleteAll());
    }
    [Fact]
    public async Task UpdateAllDistanceOk()
    {
        this._service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, 100)).ReturnsAsync(DistanceUpdateResult.None with { Updated = 2 });
        Assert.IsType<OkObjectResult>(await this._sut.UpdateAllDistance(100));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("invasion")]
    public async Task UpdateOpensTheRuleOfTheStatedKindAndPassesTheKindOn(string kind)
    {
        var existing = new Invasion { Uid = 7, GruntType = kind == "event" ? "kecleon" : "water" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 7, kind)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync("123456789", existing, kind)).ReturnsAsync(existing);

        Assert.IsType<OkObjectResult>(await this._sut.Update(7, new InvasionUpdate { Distance = 500 }, kind));

        this._service.Verify(s => s.UpdateAsync("123456789", existing, kind), Times.Once);
        this._service.Verify(s => s.GetByUidAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bogus")]
    public async Task UpdateWithoutAKnownKindKeepsTheOldLookup(string? kind)
    {
        var existing = new Invasion { Uid = 7, GruntType = "water" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 7)).ReturnsAsync(existing);
        this._service.Setup(s => s.UpdateAsync("123456789", existing)).ReturnsAsync(existing);

        Assert.IsType<OkObjectResult>(await this._sut.Update(7, new InvasionUpdate { Distance = 500 }, kind));

        this._service.Verify(s => s.UpdateAsync("123456789", existing), Times.Once);
    }

    [Theory]
    [InlineData("event", "kecleon")]
    [InlineData("invasion", "water")]
    public async Task DeleteWithAKindFindsAndDeletesTheRuleOfThatKindOnly(string kind, string grunt)
    {
        // An invasion and an event share uid 7; the stated kind picks which one goes.
        var row = new Invasion { Uid = 7, GruntType = grunt, Id = "123456789" };
        this._service.Setup(s => s.GetByUidAsync("123456789", 7, kind)).ReturnsAsync(row);
        this._service.Setup(s => s.DeleteAsync("123456789", 7, kind)).ReturnsAsync(true);

        Assert.IsType<NoContentResult>(await this._sut.Delete(7, kind));

        this._service.Verify(s => s.DeleteAsync("123456789", 7, kind), Times.Once);
        this._service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        this._service.Verify(s => s.GetByUidAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteWithAKindAnswersNotFoundWhenNoRuleOfThatKindHasTheUid()
    {
        // Only an invasion holds uid 7, so asking to delete the event must not touch it.
        this._service.Setup(s => s.GetByUidAsync("123456789", 7, "event")).ReturnsAsync((Invasion?)null);

        Assert.IsType<NotFoundResult>(await this._sut.Delete(7, "event"));

        this._service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
        this._service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bogus")]
    public async Task DeleteWithoutAKnownKindBehavesAsBefore(string? kind)
    {
        this._service.Setup(s => s.GetByUidAsync("123456789", 7)).ReturnsAsync(new Invasion { Uid = 7, Id = "123456789" });
        this._service.Setup(s => s.DeleteAsync("123456789", 7)).ReturnsAsync(true);

        Assert.IsType<NoContentResult>(await this._sut.Delete(7, kind));

        this._service.Verify(s => s.DeleteAsync("123456789", 7), Times.Once);
        this._service.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("invasion")]
    public async Task BulkDistanceWithAKindPassesItToTheService(string kind)
    {
        this._service.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), "123456789", 500, kind)).ReturnsAsync(new DistanceUpdateResult(1, [], []));

        Assert.IsType<OkObjectResult>(await this._sut.UpdateBulkDistance(new BulkDistanceRequest { Uids = [7], Distance = 500, Kind = kind }));

        this._service.Verify(s => s.UpdateDistanceByUidsAsync(It.Is<List<int>>(l => l.SequenceEqual(new[] { 7 })), "123456789", 500, kind), Times.Once);
        this._service.Verify(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BulkDistanceWithoutAKindKeepsTheUidOnlyCall()
    {
        this._service.Setup(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), "123456789", 500)).ReturnsAsync(new DistanceUpdateResult(1, [], []));

        Assert.IsType<OkObjectResult>(await this._sut.UpdateBulkDistance(new BulkDistanceRequest { Uids = [7], Distance = 500 }));

        this._service.Verify(s => s.UpdateDistanceByUidsAsync(It.IsAny<List<int>>(), "123456789", 500), Times.Once);
    }
}
