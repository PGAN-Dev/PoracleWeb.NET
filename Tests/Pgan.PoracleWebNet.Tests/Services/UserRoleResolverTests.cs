using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The resolver must distinguish "resolved: not an admin" from "could not resolve". Treating the second
/// as the first meant a PoracleNG blip during a profile switch stripped admin for the rest of the
/// session, and cached that answer for a minute. See #656.
/// </summary>
public class UserRoleResolverTests
{
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();
    private readonly Mock<IHumanService> _humanService = new();

    private const string TeamHarmonyRares = "https://discordapp.com/api/webhooks/863186716952100874/token";

    private UserRoleResolver CreateSut(string adminIds = "") => new(
        this._poracleApiProxy.Object,
        this._webhookDelegateService.Object,
        this._humanService.Object,
        Options.Create(new PoracleSettings { AdminIds = adminIds }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<UserRoleResolver>.Instance);

    [Fact]
    public async Task AnUnreachablePoracleIsReportedAsUnresolvedRatherThanAsNotAnAdmin()
    {
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.False(roles.Resolved);
    }

    [Fact]
    public async Task ADegradedAnswerIsNotCached()
    {
        // Caching it would hold the user at the wrong privilege level for the full minute after a
        // momentary outage.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        this._poracleApiProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Exactly(2));
    }

    [Fact]
    public async Task AConfiguredAdminNeedsNoNetworkAndIsAlwaysResolved()
    {
        var roles = await this.CreateSut("u1,u2").ResolveAsync("u1");

        Assert.True(roles.IsAdmin);
        Assert.True(roles.Resolved);
        this._poracleApiProxy.Verify(p => p.GetConfigAsync(), Times.Never);
    }

    [Fact]
    public async Task AGenuineNonAdminIsResolvedAndCached()
    {
        // The legitimate-case-still-passes half: a clean "no" must still be a usable answer, and must
        // still be cached.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync("{}");
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        var roles = await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        Assert.False(roles.IsAdmin);
        Assert.True(roles.Resolved);
        this._poracleApiProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Once);
    }
    /// <summary>
    /// The defect: PoracleNG returns whatever key the operator wrote in <c>[[discord.webhook_admins]]</c>,
    /// and upstream that key is the webhook's NAME -- the bot looks it up with <c>LookupWebhookByName</c>.
    /// Verified against prod 5.1.0, where <c>getAdministrationRoles</c> for a live delegate answers
    /// <c>{"webhooks":["teamharmonyrares", ...]}</c>. Both consumers compare the strings to
    /// <c>humans.id</c>, so the delegate got a nav item, an empty table and a 403. See #797.
    /// </summary>
    [Fact]
    public async Task AWebhookNamedByNameResolvesToItsId()
    {
        this.ArrangeDelegate(["teamharmonyrares"]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedWebhooks);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// The legitimate-case-still-passes half. A grant written as the webhook URL -- what the admin dialog
    /// stores in <c>poracle_web.webhook_delegates</c>, and what a PoracleJS-style config carries -- must
    /// survive canonicalisation untouched.
    /// </summary>
    [Fact]
    public async Task AWebhookNamedByIdIsKept()
    {
        this.ArrangeDelegate([TeamHarmonyRares]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedWebhooks);
    }

    [Fact]
    public async Task TheSameWebhookNamedBothWaysIsListedOnce()
    {
        this.ArrangeDelegate(["teamharmonyrares", TeamHarmonyRares]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedWebhooks);
    }

    /// <summary>
    /// A grant naming no webhook at all is dropped. It could never match a human row downstream, so
    /// carrying it rendered the My Webhooks nav item onto a page with nothing on it -- which is exactly
    /// what a config whose targets were mangled by the JSON-to-TOML conversion produces.
    /// </summary>
    [Fact]
    public async Task AGrantNamingNothingIsDropped()
    {
        this.ArrangeDelegate(["https://discordapp.com/api/webhooks/863186716952100874/to_ken"]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedWebhooks);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// The lookup is a fourth source, and a blip in it must not strip a legitimate delegate for the cache
    /// TTL -- the shape of #667. The raw grants are kept and the answer marked unresolved, so the caller
    /// falls back to the claim rather than to nothing.
    /// </summary>
    [Fact]
    public async Task AFailedWebhookLookupKeepsTheGrantsAndReportsUnresolved()
    {
        this.ArrangeDelegate(["teamharmonyrares"]);
        this._humanService.Setup(h => h.GetWebhooksAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal(["teamharmonyrares"], roles.ManagedWebhooks);
        Assert.False(roles.Resolved);
    }

    private void ArrangeDelegate(string[] grants)
    {
        var payload = JsonSerializer.Serialize(new
        {
            admin = new { discord = new { channels = Array.Empty<string>(), webhooks = grants, users = false } },
            status = "ok",
        });

        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync(payload);
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        this._humanService.Setup(h => h.GetWebhooksAsync()).ReturnsAsync(
        [
            new Human { Id = TeamHarmonyRares, Name = "teamharmonyrares", Type = "webhook" },
            new Human { Id = "https://discordapp.com/api/webhooks/863186778525925396/other", Name = "100iv", Type = "webhook" },
        ]);
    }
}
