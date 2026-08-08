using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG reports exactly what it did with a create — <c>alreadyPresent</c>, <c>updates</c>,
/// <c>insert</c> and <c>newUids</c>. Reading almost none of it was the shared root of #459, #462, #463,
/// #468 and #469, two of which destroyed user data.
/// </summary>
public class CreateResultSemanticsTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    public CreateResultSemanticsTests() =>
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

    private static JsonElement Rows(params object[] rows) => JsonSerializer.SerializeToElement(rows);

    // ── The record itself ───────────────────────────────────────────────────

    [Fact]
    public void PrimaryUidIsNullWhenPoracleNgNamedNoRow()
    {
        Assert.Null(new TrackingCreateResult([], 1, 0, 0).PrimaryUid);
        Assert.Equal(42, new TrackingCreateResult([42], 0, 0, 1).PrimaryUid);
    }

    [Fact]
    public void InsertedNothingDistinguishesAMatchFromACreate()
    {
        Assert.True(new TrackingCreateResult([7], 0, 1, 0).InsertedNothing);
        Assert.False(new TrackingCreateResult([7], 0, 0, 1).InsertedNothing);
    }

    [Fact]
    public void AnExactDuplicateIsRecognisable()
    {
        Assert.True(new TrackingCreateResult([], 1, 0, 0).WasRejectedAsDuplicate);
        Assert.False(new TrackingCreateResult([7], 0, 0, 1).WasRejectedAsDuplicate);
    }

    // ── #463: a refused edit must not report success ─────────────────────────

    [Fact]
    public async Task AnEditRefusedAsADuplicateRaisesAConflictRatherThanEchoingTheRequest()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        // PoracleNG declined: the edited values collide with another alarm the user already has.
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Gym { Uid = 134, Team = 2 }));
    }

    [Fact]
    public async Task AnOrdinaryEditIsUnaffected()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([140], 0, 1, 0));

        var result = await sut.UpdateAsync("u1", new Gym { Uid = 139, Team = 2 });

        Assert.Equal(140, result.Uid);
    }

    // ── #462: a colliding natural-key edit must not destroy either alarm ─────

    [Fact]
    public async Task EditingALureOntoAnotherAlarmsLureIdIsRefusedBeforeAnythingIsDeleted()
    {
        var sut = new LureService(this._proxy.Object, this._gate.Object,
            NullLogger<LureService>.Instance, this._remapper.Object);
        // The user holds two lures; the edit would move uid 10 onto uid 11's lure_id.
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows(
            new { uid = 10, id = "u1", lure_id = 501, distance = 500 },
            new { uid = 11, id = "u1", lure_id = 502, distance = 500 }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Lure { Uid = 10, LureId = 502 }));

        // Nothing may be deleted: the destructive step is what lost the alarm.
        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task EditingALureWithoutChangingItsLureIdStillWorks()
    {
        var sut = new LureService(this._proxy.Object, this._gate.Object,
            NullLogger<LureService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows(
            new { uid = 10, id = "u1", lure_id = 501, distance = 500 }));
        this._proxy.Setup(p => p.CreateAsync("lure", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([12], 0, 0, 1));

        var result = await sut.UpdateAsync("u1", new Lure { Uid = 10, LureId = 501, Distance = 900 });

        Assert.Equal(12, result.Uid);
    }

    [Fact]
    public async Task EditingAnInvasionOntoAnotherAlarmsGenderAndGruntIsRefused()
    {
        var sut = new InvasionService(this._proxy.Object, this._gate.Object,
            NullLogger<InvasionService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(Rows(
            new { uid = 20, id = "u1", gender = 1, grunt_type = "fire", distance = 500 },
            new { uid = 21, id = "u1", gender = 2, grunt_type = "fire", distance = 500 }));

        // Flipping uid 20's gender to 2 collides with uid 21 - reachable from the gender dropdown.
        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Invasion { Uid = 20, Gender = 2, GruntType = "fire" }));

        this._proxy.Verify(p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
