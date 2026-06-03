using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class CleanFlagsTests
{
    [Fact]
    public void ConstantsMatchPoracleNgBitmask()
    {
        Assert.Equal(1, CleanFlags.AutoDelete);
        Assert.Equal(2, CleanFlags.Edit);
        Assert.Equal(4, CleanFlags.Summary);
        Assert.Equal(7, CleanFlags.All);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(7, true)]
    public void IsAutoDeleteReadsBit1(int clean, bool expected) => Assert.Equal(expected, CleanFlags.IsAutoDelete(clean));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    public void IsEditReadsBit2(int clean, bool expected) => Assert.Equal(expected, CleanFlags.IsEdit(clean));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    public void IsSummaryReadsBit4(int clean, bool expected) => Assert.Equal(expected, CleanFlags.IsSummary(clean));

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(true, true, false, 3)]
    [InlineData(false, false, true, 4)]
    [InlineData(true, false, true, 5)]
    [InlineData(true, true, true, 7)]
    public void ComposeBuildsBitmask(bool autoDelete, bool edit, bool summary, int expected) =>
        Assert.Equal(expected, CleanFlags.Compose(autoDelete, edit, summary));

    [Theory]
    // Clearing the auto-delete bit on clean=5 (auto-delete + summary) leaves summary (4) intact.
    [InlineData(5, 1, 0, 4)]
    // Setting the auto-delete bit on clean=4 (summary only) yields 5.
    [InlineData(4, 1, 1, 5)]
    // Replacing only the edit bit leaves the auto-delete bit alone.
    [InlineData(1, 2, 2, 3)]
    // Changes outside the mask are ignored: mask is bit1 only, so bit4 in changes is dropped.
    [InlineData(0, 1, 4, 0)]
    // No-op when mask is empty.
    [InlineData(5, 0, 7, 5)]
    public void PreserveReplacesOnlyMaskedBits(int existing, int mask, int changes, int expected) =>
        Assert.Equal(expected, CleanFlags.Preserve(existing, mask, changes));
}
