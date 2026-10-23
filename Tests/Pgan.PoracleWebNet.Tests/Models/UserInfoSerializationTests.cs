using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class UserInfoSerializationTests
{
    /// <summary>
    /// The SPA reads <c>managedDestinations</c> off <c>/api/auth/me</c>. The JWT claim keeps its old
    /// <c>managedWebhooks</c> name; the response body does not.
    /// </summary>
    [Fact]
    public void ManagedDestinationsIsSentUnderItsOwnName()
    {
        var json = JsonSerializer.Serialize(new UserInfo { ManagedDestinations = ["d1"] }, JsonSerializerOptions.Web);

        Assert.Contains("\"managedDestinations\":[\"d1\"]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("managedWebhooks", json, StringComparison.Ordinal);
    }
}
