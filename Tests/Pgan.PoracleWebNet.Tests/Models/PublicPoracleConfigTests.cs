using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// <see cref="PublicPoracleConfig"/> is an allowlist, not a projection of everything, because the same
/// data was protected on one route and public on another. A field only reaches the browser by being
/// named here, which is why adding one needs a test rather than a glance at the diff.
/// </summary>
public class PublicPoracleConfigTests
{
    /// <summary>
    /// The prefix is how the help page names the real command. Clearing test DMs is something only the
    /// bot can do -- it sent them, and this site never sees the message -- so the FAQ tells the reader to
    /// send it `{prefix}poracle-clean`. A wrong prefix is worse than no advice at all. See #854.
    /// </summary>
    [Fact]
    public void TheBotCommandPrefixReachesTheBrowser()
    {
        var source = new PoracleConfig { Prefix = "$!" };

        Assert.Equal("$!", PublicPoracleConfig.From(source).Prefix);
    }

    [Fact]
    public void AnAbsentPrefixIsEmptyRatherThanNull()
    {
        Assert.Equal(string.Empty, PublicPoracleConfig.From(new PoracleConfig()).Prefix);
    }
}
