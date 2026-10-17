using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class PersonalFlagsTests
{
    [Fact]
    public void ConstantsMatchPoracleNgBitmask()
    {
        Assert.Equal(1, PersonalFlags.Shiny);
        Assert.Equal(2, PersonalFlags.Background);
        Assert.Equal(3, PersonalFlags.Known);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void IsShinyReadsBit1(int personal, bool expected) => Assert.Equal(expected, PersonalFlags.IsShiny(personal));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public void IsBackgroundReadsBit2(int personal, bool expected) => Assert.Equal(expected, PersonalFlags.IsBackground(personal));

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(8, true)]
    [InlineData(15, true)]
    public void HasUnknownBitsDetectsReservedBits(int personal, bool expected) => Assert.Equal(expected, PersonalFlags.HasUnknownBits(personal));

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 3)]
    public void ComposeBuildsMask(bool shiny, bool background, int expected) => Assert.Equal(expected, PersonalFlags.Compose(shiny, background));

    [Fact]
    public void PreserveKeepsReservedBitWhenClearingBothKnownBits() => Assert.Equal(4, PersonalFlags.Preserve(5, PersonalFlags.Known, 0));

    [Fact]
    public void PreserveKeepsReservedBitWhenSettingBothKnownBits() => Assert.Equal(7, PersonalFlags.Preserve(4, PersonalFlags.Known, 3));

    [Fact]
    public void PreserveIgnoresChangesOutsideTheMask() => Assert.Equal(1, PersonalFlags.Preserve(1, PersonalFlags.Known, 1 | 4));
}
