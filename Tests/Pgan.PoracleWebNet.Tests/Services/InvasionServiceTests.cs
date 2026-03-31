using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class InvasionServiceTests
{
    private readonly Mock<IInvasionRepository> _repository = new();
    private readonly InvasionService _sut;

    public InvasionServiceTests() => this._sut = new InvasionService(this._repository.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsInvasions()
    {
        this._repository.Setup(r => r.GetByUserAsync("u1", 1)).ReturnsAsync([new() { Uid = 1 }]);
        Assert.Single(await this._sut.GetByUserAsync("u1", 1));
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(1)).ReturnsAsync(new Invasion { Uid = 1 });
        Assert.NotNull(await this._sut.GetByUidAsync(1));
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        this._repository.Setup(r => r.GetByUidAsync(999)).ReturnsAsync((Invasion?)null);
        Assert.Null(await this._sut.GetByUidAsync(999));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.CreateAsync("user1", new Invasion());
        Assert.Equal("user1", result.Id);
    }

    [Fact]
    public async Task UpdateAsyncDelegates()
    {
        var i = new Invasion { Uid = 1 };
        this._repository.Setup(r => r.UpdateAsync(i)).ReturnsAsync(i);
        await this._sut.UpdateAsync(i);
        this._repository.Verify(r => r.UpdateAsync(i), Times.Once);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._repository.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
        Assert.True(await this._sut.DeleteAsync(1));
    }

    [Fact]
    public async Task DeleteAsyncFalse()
    {
        this._repository.Setup(r => r.DeleteAsync(999)).ReturnsAsync(false);
        Assert.False(await this._sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncCount()
    {
        this._repository.Setup(r => r.DeleteAllByUserAsync("u", 1)).ReturnsAsync(6);
        Assert.Equal(6, await this._sut.DeleteAllByUserAsync("u", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        this._repository.Setup(r => r.UpdateDistanceByUserAsync("u", 1, 50)).ReturnsAsync(4);
        Assert.Equal(4, await this._sut.UpdateDistanceByUserAsync("u", 1, 50));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        this._repository.Setup(r => r.CountByUserAsync("u", 1)).ReturnsAsync(12);
        Assert.Equal(12, await this._sut.CountByUserAsync("u", 1));
    }

    [Fact]
    public async Task CreateAsyncLowercasesGruntType()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.CreateAsync("u1", new Invasion { GruntType = "Mixed_Case_Grunt" });
        Assert.Equal("mixed_case_grunt", result.GruntType);
    }

    [Fact]
    public async Task CreateAsyncDefaultsEmptyGruntTypeToEverything()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.CreateAsync("u1", new Invasion { GruntType = "" });
        Assert.Equal("everything", result.GruntType);
    }

    [Fact]
    public async Task CreateAsyncDefaultsNullGruntTypeToEverything()
    {
        this._repository.Setup(r => r.CreateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.CreateAsync("u1", new Invasion { GruntType = null });
        Assert.Equal("everything", result.GruntType);
    }

    [Fact]
    public async Task UpdateAsyncLowercasesGruntType()
    {
        var invasion = new Invasion { Uid = 1, GruntType = "UPPER_GRUNT" };
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.UpdateAsync(invasion);
        Assert.Equal("upper_grunt", result.GruntType);
    }

    [Fact]
    public async Task UpdateAsyncDefaultsEmptyGruntTypeToEverything()
    {
        var invasion = new Invasion { Uid = 1, GruntType = "" };
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.UpdateAsync(invasion);
        Assert.Equal("everything", result.GruntType);
    }

    [Fact]
    public async Task UpdateAsyncDefaultsNullGruntTypeToEverything()
    {
        var invasion = new Invasion { Uid = 1, GruntType = null };
        this._repository.Setup(r => r.UpdateAsync(It.IsAny<Invasion>())).ReturnsAsync((Invasion i) => i);
        var result = await this._sut.UpdateAsync(invasion);
        Assert.Equal("everything", result.GruntType);
    }

    [Fact]
    public async Task BulkCreateAsyncNormalizesGruntTypes()
    {
        this._repository.Setup(r => r.BulkCreateAsync(It.IsAny<IEnumerable<Invasion>>()))
            .ReturnsAsync((IEnumerable<Invasion> items) => items);
        var models = new List<Invasion>
        {
            new() { GruntType = "UPPER" },
            new() { GruntType = "" },
            new() { GruntType = null },
        };
        var results = (await this._sut.BulkCreateAsync("u1", models)).ToList();
        Assert.Equal("upper", results[0].GruntType);
        Assert.Equal("everything", results[1].GruntType);
        Assert.Equal("everything", results[2].GruntType);
    }
}
