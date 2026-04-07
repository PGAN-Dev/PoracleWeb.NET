using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class FortChangeServiceTests
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly FortChangeService _sut;
    private static readonly string[] stringArray = ["name", "location"];

    public FortChangeServiceTests() => this._sut = new FortChangeService(this._proxy.Object);

    [Fact]
    public async Task GetByUserAsyncReturnsFortChanges()
    {
        var json = CreateJsonArray(new
        {
            uid = 1,
            id = "u1",
            fort_type = "pokestop",
            include_empty = 0,
            change_types = stringArray,
            distance = 1000
        });
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        var result = (await this._sut.GetByUserAsync("u1", 1)).ToList();
        Assert.Single(result);
        Assert.Equal("pokestop", result[0].FortType);
    }

    [Fact]
    public async Task GetByUidAsyncFound()
    {
        var json = CreateJsonArray(new
        {
            uid = 5,
            id = "u1",
            fort_type = "gym"
        });
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        var result = await this._sut.GetByUidAsync("u1", 5);
        Assert.NotNull(result);
        Assert.Equal(5, result!.Uid);
    }

    [Fact]
    public async Task GetByUidAsyncNotFound()
    {
        var json = CreateJsonArray(new
        {
            uid = 5,
            id = "u1"
        });
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        Assert.Null(await this._sut.GetByUidAsync("u1", 99));
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._proxy.Setup(p => p.CreateAsync("fort", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([10L], 0, 0, 1));
        var model = new FortChange { FortType = "pokestop" };
        var result = await this._sut.CreateAsync("u1", model);
        Assert.Equal("u1", result.Id);
        Assert.Equal(10, result.Uid);
    }

    [Fact]
    public async Task DeleteAsyncTrue()
    {
        this._proxy.Setup(p => p.DeleteByUidAsync("fort", "u1", 1)).Returns(Task.CompletedTask);
        Assert.True(await this._sut.DeleteAsync("u1", 1));
    }

    [Fact]
    public async Task DeleteAllByUserAsyncCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u1"
            },
            new
            {
                uid = 2,
                id = "u1"
            },
            new
            {
                uid = 3,
                id = "u1"
            });
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.BulkDeleteByUidsAsync("fort", "u1", It.IsAny<IEnumerable<int>>()))
            .Returns(Task.CompletedTask);
        Assert.Equal(3, await this._sut.DeleteAllByUserAsync("u1", 1));
    }

    [Fact]
    public async Task UpdateDistanceByUserAsyncCount()
    {
        var json = CreateJsonArray(
            new
            {
                uid = 1,
                id = "u1",
                distance = 0
            },
            new
            {
                uid = 2,
                id = "u1",
                distance = 0
            });
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        this._proxy.Setup(p => p.CreateAsync("fort", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 2, 0));
        Assert.Equal(2, await this._sut.UpdateDistanceByUserAsync("u1", 1, 5000));
    }

    [Fact]
    public async Task CountByUserAsyncCount()
    {
        var items = Enumerable.Range(1, 7).Select(i => new { uid = i, id = "u1" }).ToArray();
        var json = CreateJsonArray(items);
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(json);
        Assert.Equal(7, await this._sut.CountByUserAsync("u1", 1));
    }

    [Theory]
    [InlineData("pokestop", true)]
    [InlineData("gym", true)]
    [InlineData("everything", true)]
    [InlineData("POKESTOP", false)]
    [InlineData("bogus", false)]
    public void FortChangeCreateValidatesFortType(string fortType, bool expected)
    {
        var model = new FortChangeCreate { FortType = fortType, ChangeTypes = [] };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData(new[] { "name", "location" }, true)]
    [InlineData(new[] { "image_url", "removal", "new" }, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "name", "bogus" }, false)]
    public void FortChangeCreateValidatesChangeTypes(string[] changeTypes, bool expected)
    {
        var model = new FortChangeCreate { FortType = "everything", ChangeTypes = [.. changeTypes] };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        Assert.Equal(expected, isValid);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }
}
