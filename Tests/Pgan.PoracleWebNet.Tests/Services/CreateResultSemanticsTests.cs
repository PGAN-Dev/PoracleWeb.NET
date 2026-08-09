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

    // ── #498, #499, #501: a row colliding with ITSELF is a no-op, not a conflict ────

    /// <summary>
    /// PoracleNG answers {alreadyPresent:1, insert:0, updates:0} both when the edit collides with a
    /// different alarm and when it collides with the row being edited. Every edit dialog resubmits the
    /// whole form, so pressing Save with nothing changed hit the second case and was told a non-existent
    /// alarm was in the way.
    /// </summary>
    [Fact]
    public async Task ResubmittingAnUnchangedRowIsNotAConflict()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 134, id = "u1", team = 2, distance = 500, template = "1" }));

        var result = await sut.UpdateAsync("u1", new Gym
        {
            Id = "u1",
            Uid = 134,
            Team = 2,
            Distance = 500,
            Template = "1",
        });

        Assert.Equal(134, result.Uid);
    }

    /// <summary>
    /// ping is never persisted on any tracking type, so an edit changing only that leaves the row
    /// untouched. It must not read as a collision with another alarm.
    /// </summary>
    [Fact]
    public async Task APingOnlyEditIsNotAConflict()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 134, id = "u1", team = 2, distance = 500, ping = "" }));

        var result = await sut.UpdateAsync("u1", new Gym
        {
            Id = "u1",
            Uid = 134,
            Team = 2,
            Distance = 500,
            Ping = "<@&999>",
        });

        Assert.Equal(134, result.Uid);
    }

    /// <summary>A real collision with a different alarm still has to be reported.</summary>
    [Fact]
    public async Task AnEditOntoAnotherAlarmsSettingsIsStillAConflict()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));
        // The row being edited still holds team 2; the submission moves it onto uid 135's team 4.
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 134, id = "u1", team = 2, distance = 500 },
            new { uid = 135, id = "u1", team = 4, distance = 500 }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Gym { Id = "u1", Uid = 134, Team = 4, Distance = 500 }));
    }

    /// <summary>A vanished row is not a no-op: report the conflict rather than claim success.</summary>
    [Fact]
    public async Task AConflictIsStillReportedWhenTheEditedRowIsGone()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([], 1, 0, 0));
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 135, id = "u1", team = 4, distance = 500 }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Gym { Id = "u1", Uid = 134, Team = 4, Distance = 500 }));
    }
    // ── #531: an edit must never be satisfied by merging into a DIFFERENT alarm ────

    /// <summary>
    /// PoracleNG updates an existing row in place when the submission differs from it only in fields it
    /// tags updatable -- distance, template, clean, and slot/battle changes on gyms. If that row is not the
    /// one being edited, the edit overwrites somebody else's alarm and the reconciler then deletes the
    /// original as superseded: two alarms become one, reported as a clean 200.
    /// </summary>
    [Fact]
    public async Task AnEditThatWouldMergeIntoAnotherAlarmIsRefusedBeforeAnythingIsWritten()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 189, id = "u1", team = 4, gym_id = "", distance = 1500 },
            new { uid = 190, id = "u1", team = 2, gym_id = "", distance = 8001 }));

        // Moving 189 onto team 2 leaves only distance differing from 190, so PoracleNG would merge them.
        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Gym { Id = "u1", Uid = 189, Team = 2, Distance = 1500 }));

        this._proxy.Verify(
            p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
        this._proxy.Verify(
            p => p.DeleteByUidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// slot_changes and battle_changes are updatable for gyms, so two alarms differing only there would
    /// merge as well -- the case the sweep reproduced from the gym edit dialog.
    /// </summary>
    [Fact]
    public async Task AGymEditDifferingOnlyInItsToggleFieldsIsAlsoRefused()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 189, id = "u1", team = 4, slot_changes = 1, battle_changes = 0, distance = 1500 },
            new { uid = 190, id = "u1", team = 4, slot_changes = 0, battle_changes = 1, distance = 8001 }));

        await Assert.ThrowsAsync<TrackingConflictException>(
            () => sut.UpdateAsync("u1", new Gym
            {
                Id = "u1",
                Uid = 189,
                Team = 4,
                SlotChanges = 0,
                BattleChanges = 1,
                Distance = 1500,
            }));
    }

    /// <summary>
    /// Two alarms that differ in more than one updatable field genuinely coexist -- PoracleNG inserts
    /// rather than merges -- so every ordinary edit on them must keep working. Ignoring those fields
    /// wholesale called such a pair a collision and refused radius, template and auto-delete edits on both,
    /// leaving them uneditable. See #553.
    /// </summary>
    [Fact]
    public async Task AnEditIsAllowedWhenTwoUpdatableFieldsSeparateTheAlarms()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 1, id = "u1", team = 4, distance = 1000, template = "1" },
            new { uid = 2, id = "u1", team = 4, distance = 6000, template = "ZZalt" }));
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([3], 0, 1, 0));

        // Changing uid 2's radius still leaves it separated from uid 1 by BOTH radius and template.
        var result = await sut.UpdateAsync("u1", new Gym
        {
            Id = "u1",
            Uid = 2,
            Team = 4,
            Distance = 6500,
            Template = "ZZalt",
        });

        Assert.Equal(3, result.Uid);
    }

    /// <summary>
    /// Gyms that differ only in their slot/battle toggles are kept apart by PoracleNG, so those fields
    /// identify an alarm and both must stay editable. See #553.
    /// </summary>
    [Fact]
    public async Task AGymSeparatedOnlyByItsTogglesIsStillEditable()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 1, id = "u1", team = 4, slot_changes = 1, battle_changes = 0, distance = 1000 },
            new { uid = 2, id = "u1", team = 4, slot_changes = 0, battle_changes = 1, distance = 1000 }));
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([3], 0, 1, 0));

        var result = await sut.UpdateAsync("u1", new Gym
        {
            Id = "u1",
            Uid = 2,
            Team = 4,
            SlotChanges = 0,
            BattleChanges = 1,
            Distance = 1500,
        });

        Assert.Equal(3, result.Uid);
    }

    /// <summary>An edit that collides with nothing must still go through untouched.</summary>
    [Fact]
    public async Task AnEditThatCollidesWithNothingIsUnaffected()
    {
        var sut = new GymService(this._proxy.Object, this._gate.Object,
            NullLogger<GymService>.Instance, this._remapper.Object);
        this._proxy.Setup(p => p.GetByUserAsync("gym", "u1")).ReturnsAsync(Rows(
            new { uid = 189, id = "u1", team = 4, distance = 1500 },
            new { uid = 190, id = "u1", team = 2, distance = 8001 }));
        this._proxy.Setup(p => p.CreateAsync("gym", "u1", It.IsAny<JsonElement>()))
            .ReturnsAsync(new TrackingCreateResult([191], 0, 1, 0));

        var result = await sut.UpdateAsync("u1", new Gym { Id = "u1", Uid = 189, Team = 3, Distance = 1500 });

        Assert.Equal(191, result.Uid);
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
