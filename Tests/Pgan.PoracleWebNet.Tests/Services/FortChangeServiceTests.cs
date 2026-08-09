using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ITrackedUidRemapper> _uidRemapper = new();
    private readonly FortChangeService _sut;
    private static readonly string[] stringArray = ["name", "location"];

    public FortChangeServiceTests()
    {
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new FortChangeService(this._proxy.Object, this._featureGate.Object, NullLogger<FortChangeService>.Instance, this._uidRemapper.Object);
    }

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

    /// <summary>
    /// PoracleNG's dedup key for this type ignores distance, so a create matching an existing alarm's
    /// settings overwrote that alarm's radius while PoracleWeb answered 201 with a fresh uid -- the user
    /// believed they had two alarms and the configured radius was gone. See #502.
    /// </summary>
    [Fact]
    public async Task CreateAsyncRefusesAnAlarmThatWouldOverwriteAnExistingOne()
    {
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(CreateJsonArray(new
        {
            uid = 37,
            id = "u1",
            fort_type = "everything",
            include_empty = 1,
            change_types = "[\"new\"]",
            distance = 700,
        }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => this._sut.CreateAsync("u1", new FortChange
            {
                FortType = "everything",
                IncludeEmpty = 1,
                ChangeTypes = ["new"],
                Distance = 900,
            }));

        this._proxy.Verify(p => p.CreateAsync("fort", "u1", It.IsAny<JsonElement>()), Times.Never);
    }

    /// <summary>Different settings still add a second alarm.</summary>
    [Fact]
    public async Task CreateAsyncAllowsADifferentFortType()
    {
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(CreateJsonArray(new
        {
            uid = 37,
            id = "u1",
            fort_type = "everything",
            include_empty = 1,
            change_types = "[\"new\"]",
        }));
        this._proxy.Setup(p => p.CreateAsync("fort", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([38L], 0, 0, 1));

        var result = await this._sut.CreateAsync("u1", new FortChange
        {
            FortType = "gym",
            IncludeEmpty = 1,
            ChangeTypes = ["new"],
        });

        Assert.Equal(38, result.Uid);
    }

    [Fact]
    public async Task CreateAsyncSetsUserId()
    {
        this._proxy.Setup(p => p.GetByUserAsync("fort", "u1")).ReturnsAsync(CreateJsonArray());
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
                distance = 0,
                template = "ZZrow1"
            },
            new
            {
                uid = 2,
                id = "u1",
                distance = 0,
                template = "ZZrow2"
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
    [InlineData(new[] { "name", "location", "image_url", "removal", "new" }, true)]
    [InlineData(new[] { "name", "name" }, false)]
    [InlineData(new[] { "name", "NAME" }, false)]
    public void FortChangeCreateRefusesRepeatedAndOverLongChangeTypes(string[] changeTypes, bool expected)
    {
        // Five legal values, so a longer list or a repeat cannot mean anything and used to reach the
        // database as an over-long JSON string. See #612.
        var model = new FortChangeCreate { FortType = "everything", ChangeTypes = [.. changeTypes] };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void FortChangeCreateRefusesMoreThanFiveChangeTypes()
    {
        var model = new FortChangeCreate
        {
            FortType = "everything",
            ChangeTypes = ["name", "location", "image_url", "removal", "new", "name2"],
        };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));
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

    [Fact]
    public async Task CreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.FortChanges))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.FortChanges));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.CreateAsync("u", new FortChange()));

        Assert.Equal(DisableFeatureKeys.FortChanges, ex.DisableKey);
        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task BulkCreateAsyncThrowsFeatureDisabledExceptionWhenGated()
    {
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.FortChanges))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.FortChanges));

        await Assert.ThrowsAsync<FeatureDisabledException>(
            () => this._sut.BulkCreateAsync("u", new List<FortChange> { new() }));

        this._proxy.Verify(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    private static JsonElement CreateJsonArray(params object[] items)
    {
        var jsonStr = JsonSerializer.Serialize(items, SnakeCaseOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.Clone();
    }

    // --- Duplicate-on-edit ---
    // PoracleNG dedups fort tracking by a natural key. When an edit changes a field in that key it INSERTS
    // instead of upserting, leaving the pre-edit row behind as a second live alarm firing the old filter.

    [Fact]
    public async Task UpdateAsyncDeletesTheSupersededRowWhenPoracleNgInsertsInsteadOfUpdating()
    {
        var model = new FortChange { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("fort", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("fort", "user1", 41)).Returns(Task.CompletedTask);

        var result = await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync("fort", "user1", 41), Times.Once);
        Assert.Equal(42, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncKeepsTheUidAndDeletesNothingWhenPoracleNgUpserts()
    {
        var model = new FortChange { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("fort", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 0, 1, 0));

        var result = await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.Equal(41, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncStillSucceedsWhenDeletingTheSupersededRowFails()
    {
        var model = new FortChange { Uid = 41 };
        this._proxy.Setup(p => p.CreateAsync("fort", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));
        this._proxy.Setup(p => p.DeleteByUidAsync("fort", "user1", 41))
            .ThrowsAsync(new HttpRequestException("boom"));

        // The inserted row already carries the user's settings, so the edit must not fail.
        var result = await this._sut.UpdateAsync("user1", model);

        Assert.Equal(42, result.Uid);
    }

    [Fact]
    public async Task UpdateAsyncOnANewRecordDoesNotAttemptAStaleDelete()
    {
        var model = new FortChange { Uid = 0 };
        this._proxy.Setup(p => p.CreateAsync("fort", "user1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([42], 0, 0, 1));

        await this._sut.UpdateAsync("user1", model);

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

}
