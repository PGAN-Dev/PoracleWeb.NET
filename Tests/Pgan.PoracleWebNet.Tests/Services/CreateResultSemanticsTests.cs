using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG reports exactly what it did with a create — <c>alreadyPresent</c>, <c>updates</c>,
/// <c>insert</c> and <c>newUids</c>. Reading almost none of it was the shared root of #459, #462, #463,
/// #468 and #469, two of which destroyed user data.
/// </summary>
public class CreateResultSemanticsTests
{
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
}
