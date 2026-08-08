using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG rewrites a tracking row on edit, so the uid changes. Quick-pick applied state stores the
/// uids captured at apply time, so without this the stored uids went stale: removal deleted nothing,
/// reported 204, and the summary read then wiped the applied state — leaving an alarm that fired forever
/// with no UI to remove it. See #403.
/// </summary>
public class TrackedUidRemapperTests
{
    private readonly Mock<IQuickPickAppliedStateRepository> _repo = new();
    private readonly TrackedUidRemapper _sut;

    public TrackedUidRemapperTests() =>
        this._sut = new TrackedUidRemapper(this._repo.Object, NullLogger<TrackedUidRemapper>.Instance);

    private static QuickPickAppliedState State(string quickPickId, string alarmType, params int[] uids) => new()
    {
        UserId = "u1",
        ProfileNo = 1,
        QuickPickId = quickPickId,
        AlarmType = alarmType,
        TrackedUids = [.. uids]
    };

    [Fact]
    public async Task RewritesTheRotatedUidInPlace()
    {
        var state = State("lure-glacial", "lure", 239);
        this._repo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([state]);

        await this._sut.RemapAsync("u1", "lure", 239, 240);

        Assert.Equal([240], state.TrackedUids);
        this._repo.Verify(r => r.CreateOrUpdateAsync(state), Times.Once);
    }

    [Fact]
    public async Task LeavesTheOtherTrackedUidsAlone()
    {
        var state = State("raid-legendary", "raid", 10, 11, 12);
        this._repo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([state]);

        await this._sut.RemapAsync("u1", "raid", 11, 99);

        Assert.Equal([10, 99, 12], state.TrackedUids);
    }

    [Fact]
    public async Task IgnoresAppliedStateForAnotherAlarmType()
    {
        // uids are only unique within a type, so a raid uid 5 must not rewrite a lure's uid 5.
        var lure = State("lure-glacial", "lure", 5);
        this._repo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([lure]);

        await this._sut.RemapAsync("u1", "raid", 5, 6);

        Assert.Equal([5], lure.TrackedUids);
        this._repo.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickAppliedState>()), Times.Never);
    }

    [Fact]
    public async Task RemapsAcrossEveryProfileTheUserHas()
    {
        // An alarm edit does not say which profile the row belongs to, and a uid is unique per user,
        // so whichever profile's state references it is the right one.
        var p2 = State("gym-team", "gym", 77);
        p2.ProfileNo = 2;
        this._repo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([p2]);

        await this._sut.RemapAsync("u1", "gym", 77, 78);

        Assert.Equal([78], p2.TrackedUids);
    }

    [Fact]
    public async Task DoesNothingWhenNoQuickPickTracksTheUid()
    {
        this._repo.Setup(r => r.GetByUserAsync("u1")).ReturnsAsync([State("quest-stardust", "quest", 1)]);

        await this._sut.RemapAsync("u1", "quest", 500, 501);

        this._repo.Verify(r => r.CreateOrUpdateAsync(It.IsAny<QuickPickAppliedState>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(7, 7)]
    public async Task SkipsNonRotations(int oldUid, int newUid)
    {
        await this._sut.RemapAsync("u1", "lure", oldUid, newUid);

        this._repo.Verify(r => r.GetByUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SwallowsRepositoryFailures()
    {
        // The edit already succeeded upstream. Failing here would fail a request that worked, and cost
        // the user their edit to save a "remove" button.
        this._repo.Setup(r => r.GetByUserAsync("u1")).ThrowsAsync(new InvalidOperationException("db down"));

        await this._sut.RemapAsync("u1", "lure", 1, 2);
    }
}
