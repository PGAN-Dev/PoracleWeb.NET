using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class UserInfoSerializationTests
{
    /// <summary>
    /// The SPA still reads <c>managedWebhooks</c> off <c>/api/auth/me</c>, so the C# rename to
    /// <c>ManagedDestinations</c> must not reach the wire. Task 6.5 of #1025 flips this assertion in the
    /// same change that renames the SPA's field.
    /// </summary>
    [Fact]
    public void ManagedDestinationsIsStillSentAsManagedWebhooks()
    {
        var json = JsonSerializer.Serialize(new UserInfo { ManagedDestinations = ["d1"] }, JsonSerializerOptions.Web);

        Assert.Contains("\"managedWebhooks\":[\"d1\"]", json, StringComparison.Ordinal);
    }
}
