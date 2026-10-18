using System.Text;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The opaque id the API hands out instead of a player id. It has to be stable (the SPA addresses an account
/// by it), distinct per human and per account, and impossible to turn back into the player id.
/// </summary>
public class PlayerAccountHandlesTests
{
    private static readonly PlayerAccountHandles Handles = PlayerAccountHandles.FromSecret("a jwt secret long enough to be one");

    [Fact]
    public void TheSameAccountAlwaysGetsTheSameHandle()
    {
        Assert.Equal(Handles.For("123", "Ab9cD"), Handles.For("123", "Ab9cD"));
        Assert.Equal(Handles.For("123", "Ab9cD"), PlayerAccountHandles.FromSecret("a jwt secret long enough to be one").For("123", "Ab9cD"));
    }

    [Fact]
    public void ADifferentAccountOrHumanGetsADifferentHandle()
    {
        var handle = Handles.For("123", "Ab9cD");

        // Player ids are case-sensitive, so a different case is a different account.
        Assert.NotEqual(handle, Handles.For("123", "ab9cd"));
        Assert.NotEqual(handle, Handles.For("456", "Ab9cD"));
        // The separator stops ("12", "3Ab9cD") colliding with ("123", "Ab9cD").
        Assert.NotEqual(handle, Handles.For("12", "3Ab9cD"));
    }

    [Fact]
    public void AnotherServersSecretGivesOtherHandles()
    {
        Assert.NotEqual(Handles.For("123", "Ab9cD"), PlayerAccountHandles.FromSecret("someone else's secret entirely").For("123", "Ab9cD"));
    }

    [Fact]
    public void TheHandleCarriesNoTraceOfThePlayerId()
    {
        var handle = Handles.For("123", "Ab9cDxYz");

        Assert.DoesNotContain("Ab9cD", handle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("Ab9cDxYz")).TrimEnd('='), handle);
    }

    [Fact]
    public void TheHandleIsSafeInAUrlPath()
    {
        var handle = Handles.For("http://hook.example/x?y=1", "Ab9cD");

        Assert.Matches("^[A-Za-z0-9_-]{22}$", handle);
    }
}
