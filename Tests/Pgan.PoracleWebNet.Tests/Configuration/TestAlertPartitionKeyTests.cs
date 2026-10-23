using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Test alerts are rate limited per person. A delegate managing several destinations impersonates each in
/// turn, and every impersonation token carries the destination's id as <c>userId</c>, so keying on that
/// gave one person a fresh five-a-minute bucket per destination. The person is <c>impersonatedBy</c>.
/// See #1025.
/// </summary>
public class TestAlertPartitionKeyTests
{
    private static DefaultHttpContext Context(string? userId, string? impersonatedBy = null, string ip = "10.0.0.1")
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim("userId", userId));
        }

        if (impersonatedBy is not null)
        {
            claims.Add(new Claim("impersonatedBy", impersonatedBy));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Count > 0 ? "Test" : null)),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public void TestAlertsShareOneBucketPerImpersonator()
    {
        var channel = RateLimitPartitionKeys.TestAlertPartitionKey(Context("1100000000000000001", impersonatedBy: "42"));
        var webhook = RateLimitPartitionKeys.TestAlertPartitionKey(Context("https://hook/a", impersonatedBy: "42"));

        Assert.Equal(channel, webhook);
    }

    [Fact]
    public void AnImpersonatorSharesTheBucketOfTheirOwnSession()
    {
        Assert.Equal(
            RateLimitPartitionKeys.TestAlertPartitionKey(Context("42")),
            RateLimitPartitionKeys.TestAlertPartitionKey(Context("1100000000000000001", impersonatedBy: "42")));
    }

    [Fact]
    public void TwoImpersonatorsOfOneDestinationGetTwoBuckets()
    {
        Assert.NotEqual(
            RateLimitPartitionKeys.TestAlertPartitionKey(Context("1100000000000000001", impersonatedBy: "42")),
            RateLimitPartitionKeys.TestAlertPartitionKey(Context("1100000000000000001", impersonatedBy: "43")));
    }

    /// <summary>The legitimate cases: an ordinary session keys on its user, and no token keys on the IP.</summary>
    [Fact]
    public void WithoutImpersonationItIsThePerUserKey()
    {
        var signedIn = Context("42");
        var anonymous = Context(null, ip: "10.0.0.9");

        Assert.Equal(RateLimitPartitionKeys.UserOrIpPartitionKey(signedIn), RateLimitPartitionKeys.TestAlertPartitionKey(signedIn));
        Assert.Equal("user:42", RateLimitPartitionKeys.TestAlertPartitionKey(signedIn));
        Assert.Equal("ip:10.0.0.9", RateLimitPartitionKeys.TestAlertPartitionKey(anonymous));
    }

    [Fact]
    public void AnEmptyImpersonatedByFallsBackToTheUser()
    {
        Assert.Equal("user:7", RateLimitPartitionKeys.TestAlertPartitionKey(Context("7", impersonatedBy: "")));
    }
}
