using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// <c>link_register_missing</c> was a boolean and became three values. What is already stored must keep its meaning:
/// "true" in any casing is <c>always</c>, and "false", empty or absent is <c>off</c>.
/// </summary>
public class LinkRegisterModeTests
{
    [Theory]
    [InlineData("true", LinkRegisterMode.Always)]
    [InlineData("True", LinkRegisterMode.Always)]
    [InlineData(" TRUE ", LinkRegisterMode.Always)]
    [InlineData("always", LinkRegisterMode.Always)]
    [InlineData("when-qualified", LinkRegisterMode.WhenQualified)]
    [InlineData("When-Qualified", LinkRegisterMode.WhenQualified)]
    [InlineData("off", LinkRegisterMode.Off)]
    [InlineData("false", LinkRegisterMode.Off)]
    [InlineData("False", LinkRegisterMode.Off)]
    [InlineData("", LinkRegisterMode.Off)]
    [InlineData(null, LinkRegisterMode.Off)]
    public void Parse_ReadsStoredValues(string? stored, LinkRegisterMode expected) =>
        Assert.Equal(expected, LinkRegisterModes.Parse(stored));

    [Theory]
    [InlineData("when_qualified")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("garbage")]
    public void Parse_AnythingUnrecognisedIsOff_NeverMoreOpen(string stored) =>
        Assert.Equal(LinkRegisterMode.Off, LinkRegisterModes.Parse(stored));

    [Theory]
    [InlineData(LinkRegisterMode.Off, "off")]
    [InlineData(LinkRegisterMode.WhenQualified, "when-qualified")]
    [InlineData(LinkRegisterMode.Always, "always")]
    public void Wire_RoundTrips(LinkRegisterMode mode, string wire)
    {
        Assert.Equal(wire, LinkRegisterModes.Wire(mode));
        Assert.Equal(mode, LinkRegisterModes.Parse(wire));
    }
}
