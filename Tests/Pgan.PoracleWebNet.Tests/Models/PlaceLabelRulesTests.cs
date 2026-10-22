using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// Place labels took any text up to 64 characters, so "Gym" + U+202E + "evil" was saved and rendered as
/// "Gymlive" -- the spoof <see cref="ProfileNameRules"/> exists to stop -- and a line break split a label
/// across two lines on every card that shows it. A label now passes the same character rule as a profile
/// name. It is applied to a new label only (adding a place); moving or deleting one addresses the label
/// already stored, so a label saved before this rule stays usable.
/// </summary>
public class PlaceLabelRulesTests
{
    [Theory]
    [InlineData("Gym\u202Eevil")]
    [InlineData("Line\nbreak")]
    [InlineData("tab\there")]
    [InlineData("nul\u0000byte")]
    [InlineData("isolate\u2066left")]
    [InlineData("line\u2028separator")]
    public void ALabelCarryingAControlOrDirectionCharacterIsRefused(string label) =>
        Assert.NotNull(PlaceLabelRules.Validate(label));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankLabelIsRefused(string? label) => Assert.NotNull(PlaceLabelRules.Validate(label));

    [Fact]
    public void ALabelOverTheColumnIsRefused() =>
        Assert.NotNull(PlaceLabelRules.Validate(new string('a', PlaceLabelRules.MaxLength + 1)));

    /// <summary>
    /// What must keep working: the labels the audit left on a test account, accents, other scripts,
    /// emoji joined with U+200D, and a label exactly at the column limit.
    /// </summary>
    [Theory]
    [InlineData("Work")]
    [InlineData("Parents' house - Maumee")]
    [InlineData("Café (2)")]
    [InlineData("שוק")]
    [InlineData("👨\u200D👩\u200D👧 family")]
    public void AnOrdinaryLabelIsAccepted(string label) => Assert.Null(PlaceLabelRules.Validate(label));

    [Fact]
    public void ALabelAtTheColumnLimitIsAccepted() =>
        Assert.Null(PlaceLabelRules.Validate(new string('a', PlaceLabelRules.MaxLength)));

    [Fact]
    public void SurroundingSpaceDoesNotCountTowardsTheLimit() =>
        Assert.Null(PlaceLabelRules.Validate($"  {new string('a', PlaceLabelRules.MaxLength)}  "));
}
