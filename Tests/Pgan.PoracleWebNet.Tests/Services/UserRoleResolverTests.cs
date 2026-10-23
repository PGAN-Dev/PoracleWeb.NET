using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The resolver must distinguish "resolved: not an admin" from "could not resolve". Treating the second
/// as the first meant a PoracleNG blip during a profile switch stripped admin for the rest of the
/// session, and cached that answer for a minute. See #656.
/// </summary>
public class UserRoleResolverTests
{
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IPoracleHumanProxy> _poracleHumanProxy = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();
    private readonly Mock<IDestinationDirectory> _directory = new();
    private readonly Mock<ISiteSettingService> _siteSettings = new();

    private const string TeamHarmonyRares = "https://discordapp.com/api/webhooks/863186716952100874/token";
    private const string RaidChannel = "1100000000000000001";
    private const string UnregisteredChannel = "1100000000000000099";
    private const string RaidThread = "1100000000000000002";
    private const string TelegramChannel = "-1001234567890";

    private UserRoleResolver CreateSut(string adminIds = "", IMemoryCache? cache = null, ILogger<UserRoleResolver>? logger = null) => new(
        this._poracleApiProxy.Object,
        this._poracleHumanProxy.Object,
        this._webhookDelegateService.Object,
        this._directory.Object,
        this._siteSettings.Object,
        Options.Create(new PoracleSettings { AdminIds = adminIds }),
        cache ?? new MemoryCache(new MemoryCacheOptions()),
        logger ?? NullLogger<UserRoleResolver>.Instance);

    [Fact]
    public async Task AnUnreachablePoracleIsReportedAsUnresolvedRatherThanAsNotAnAdmin()
    {
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
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
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        this._poracleHumanProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Exactly(2));
    }

    [Fact]
    public async Task AnAdminRolesReadThatCouldNotBeMadeIsUnresolved()
    {
        // The hole the proxy change closes. GetAdminRolesAsync used to answer null for every non-2xx,
        // so a 500 or a 503 arrived here as "administers nothing" with Resolved:true -- and got cached
        // for the full minute, which is exactly what #656 and #667 were about on the other two sources.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleHumanProxy
            .Setup(p => p.GetAdminRolesAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("503"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.False(roles.Resolved);
    }

    [Fact]
    public async Task AHumanPoracleHasNeverHeardOfIsAResolvedNo()
    {
        // The legitimate case beside it: a 404 is PoracleNG answering, not failing, so the proxy turns
        // it into null and this must stay a confident "no" rather than joining the degraded case.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.False(roles.IsAdmin);
        Assert.True(roles.Resolved);
    }

    [Fact]
    public async Task AdminStatusIsNeverTakenFromTheRolesBody()
    {
        // Two isAdmin branches used to read one. Neither could ever fire -- both API versions build the
        // body from the same adminRolesResult, whose fields are channels, webhooks and users, and v2's
        // schema is additionalProperties:false. Left in, they were a promotion path from a field
        // upstream does not send, which is a worse thing to carry than a missing feature.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleHumanProxy
            .Setup(p => p.GetAdminRolesAsync(It.IsAny<string>()))
            .ReturnsAsync("""{"isAdmin":true,"admin":{"discord":{"isAdmin":true,"webhooks":[],"users":false}}}""");
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.False(roles.IsAdmin);
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
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync("{}");
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        var roles = await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        Assert.False(roles.IsAdmin);
        Assert.True(roles.Resolved);
        this._poracleHumanProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Once);
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

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
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

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
    }

    [Fact]
    public async Task TheSameWebhookNamedBothWaysIsListedOnce()
    {
        this.ArrangeDelegate(["teamharmonyrares", TeamHarmonyRares]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
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

        Assert.Null(roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// The grant is a webhook URL, token and all, and logging it raw put a credential in the log. See #1038.
    /// </summary>
    [Fact]
    public async Task AnUnresolvedGrantIsLoggedWithoutItsWebhookToken()
    {
        this.ArrangeDelegate(["https://discordapp.com/api/webhooks/863186716952100874/to_ken"]);
        var logger = new CapturingLogger<UserRoleResolver>();

        await this.CreateSut(logger: logger).ResolveAsync("u1");

        Assert.Contains("https://discordapp.com/api/webhooks/863186716952100874/***", logger.All, StringComparison.Ordinal);
        Assert.DoesNotContain("to_ken", logger.All, StringComparison.Ordinal);
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
        this._directory.Setup(d => d.GetAsync()).ThrowsAsync(new HttpRequestException("down"));

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal(["teamharmonyrares"], roles.ManagedDestinations);
        Assert.False(roles.Resolved);
    }

    /// <summary>
    /// Granting or revoking a delegate took up to a minute to land, because the admin dialog wrote the
    /// table and left the cached answer in place. Invalidation must also reach a DIFFERENT resolver
    /// instance: it is scoped, so the admin's request and the delegate's next request each get their own,
    /// and only the memory cache is shared.
    /// </summary>
    [Fact]
    public async Task InvalidatingAUserMakesTheNextResolveReadTheTableAgain()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        this.ArrangeLocalDelegate([]);
        var delegatesRequest = this.CreateSut(cache: cache);
        Assert.Null((await delegatesRequest.ResolveAsync("u1")).ManagedDestinations);

        this.ArrangeLocalDelegate([TeamHarmonyRares]);
        this.CreateSut(cache: cache).Invalidate("u1");

        Assert.Equal([TeamHarmonyRares], (await this.CreateSut(cache: cache).ResolveAsync("u1")).ManagedDestinations);
    }

    [Fact]
    public async Task InvalidatingEveryoneReachesAUserNobodyNamed()
    {
        // Deleting a webhook changes the answer for every delegate who held it, including the ones whose
        // grant came from PoracleJS by name and so appear in no table this site can enumerate.
        var cache = new MemoryCache(new MemoryCacheOptions());
        this.ArrangeLocalDelegate([TeamHarmonyRares]);
        Assert.NotNull((await this.CreateSut(cache: cache).ResolveAsync("u1")).ManagedDestinations);

        this.ArrangeLocalDelegate([]);
        this.CreateSut(cache: cache).InvalidateAll();

        Assert.Null((await this.CreateSut(cache: cache).ResolveAsync("u1")).ManagedDestinations);
    }

    [Fact]
    public async Task InvalidatingOneUserLeavesAnotherCached()
    {
        // The legitimate-case half: invalidation is targeted, not a flush, so a grant does not put two
        // PoracleNG calls in front of everyone's next request.
        var cache = new MemoryCache(new MemoryCacheOptions());
        this.ArrangeLocalDelegate([]);
        await this.CreateSut(cache: cache).ResolveAsync("u1");
        await this.CreateSut(cache: cache).ResolveAsync("u2");

        this.CreateSut(cache: cache).Invalidate("u1");
        await this.CreateSut(cache: cache).ResolveAsync("u2");

        this._poracleHumanProxy.Verify(p => p.GetAdminRolesAsync("u2"), Times.Once);
    }

    private void ArrangeLocalDelegate(string[] webhookIds)
    {
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync("{}");
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync(webhookIds);
        this.ArrangeDirectory();
    }

    private void ArrangeDelegate(
        string[] grants,
        string[]? channels = null,
        string[]? telegramChannels = null,
        string[]? localGrants = null,
        bool users = false)
    {
        var payload = JsonSerializer.Serialize(new
        {
            admin = new
            {
                discord = new { channels = channels ?? [], webhooks = grants, users },
                telegram = new { channels = telegramChannels ?? [], users },
            },
            status = "ok",
        });

        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleHumanProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync(payload);
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync(localGrants ?? []);
        this.ArrangeDirectory();
    }

    /// <summary>
    /// The directory as PoracleNG would list it: two webhooks, a channel, a thread whose name collides with
    /// a grant, and a Telegram channel. Passing rows replaces that list.
    /// </summary>
    private void ArrangeDirectory(params Human[] rows) =>
        this._directory.Setup(d => d.GetAsync()).ReturnsAsync(rows.Length > 0 ? rows :
        [
            new Human { Id = TeamHarmonyRares, Name = "teamharmonyrares", Type = HumanKind.Webhook },
            new Human { Id = "https://discordapp.com/api/webhooks/863186778525925396/other", Name = "100iv", Type = HumanKind.Webhook },
            new Human { Id = RaidChannel, Name = "raid-alerts", Type = HumanKind.DiscordChannel },
            new Human { Id = RaidThread, Name = "thread-only", Type = HumanKind.DiscordThread },
            new Human { Id = TelegramChannel, Name = "tg-rares", Type = HumanKind.TelegramChannel },
        ]);

    private void SettingIs(bool on) =>
        this._siteSettings.Setup(s => s.GetBoolAsync(UserRoleResolver.PoracleChannelsSettingKey)).ReturnsAsync(on);

    // --- Part 6 of #1025: delegation over every destination ---

    /// <summary>
    /// Review Focus 4. All six production grants are webhook grants written before the directory replaced
    /// <c>GetWebhooksAsync</c>; by name and by id they must resolve exactly as they did.
    /// </summary>
    [Fact]
    public async Task AnExistingWebhookGrantStillResolves()
    {
        this.ArrangeDelegate(["teamharmonyrares"], localGrants: [TeamHarmonyRares]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    [Fact]
    public async Task ALocalGrantOnAChannelResolves()
    {
        this.ArrangeDelegate([], localGrants: [RaidChannel]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([RaidChannel], roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// Upstream resolves a <c>webhook_admins</c> target by name over webhooks, channels and Telegram
    /// channels and groups alike, so a name that belongs to a channel is a grant on that channel.
    /// </summary>
    [Fact]
    public async Task AWebhookAdminsNameThatMatchesAChannelResolves()
    {
        this.ArrangeDelegate(["raid-alerts", "tg-rares"]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal(
            new[] { RaidChannel, TelegramChannel }.Order(StringComparer.Ordinal),
            roles.ManagedDestinations!.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// <c>admin.discord.channels</c> lists every channel the user may admin in Discord itself, which is not
    /// the same thing as having been handed it on this site. It counts only when the operator says so.
    /// </summary>
    [Fact]
    public async Task PoraclesChannelListsAreIgnoredWhileTheSettingIsOff()
    {
        this.ArrangeDelegate([], channels: [RaidChannel], telegramChannels: [TelegramChannel]);
        this.SettingIs(false);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    [Fact]
    public async Task PoraclesChannelListsCountOnceTheSettingIsOn()
    {
        // users:true says the person may admin Discord users; it grants nothing here.
        this.ArrangeDelegate([], channels: [RaidChannel, UnregisteredChannel, RaidChannel], users: true);
        this.SettingIs(true);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([RaidChannel], roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    [Fact]
    public async Task TelegramChannelListsCountOnceTheSettingIsOn()
    {
        this.ArrangeDelegate([], telegramChannels: [TelegramChannel]);
        this.SettingIs(true);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TelegramChannel], roles.ManagedDestinations);
    }

    /// <summary>
    /// The channel lists hold ids. A list entry that happens to equal a destination's name must not grant
    /// it, or naming a channel after another one would hand that one over.
    /// </summary>
    [Fact]
    public async Task PoraclesChannelListsMatchByIdOnly()
    {
        this.ArrangeDelegate([], channels: ["raid-alerts"]);
        this.SettingIs(true);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedDestinations);
    }

    /// <summary>
    /// Upstream resolves names over webhooks, channels and Telegram channels and groups, never threads or
    /// topics, so a thread whose name equals a grant is not what the grant meant.
    /// </summary>
    [Fact]
    public async Task ANameNeverResolvesToAThread()
    {
        this.ArrangeDelegate(["thread-only"]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// Upstream's name lookup has no ORDER BY, so which row a shared name picks there is arbitrary. Here
    /// it is fixed: a webhook wins, because every grant written before #1025 meant a webhook.
    /// </summary>
    [Fact]
    public async Task AWebhookWinsANameSharedWithAChannel()
    {
        this.ArrangeDelegate(["shared"]);
        this.ArrangeDirectory(
            new Human { Id = RaidChannel, Name = "shared", Type = HumanKind.DiscordChannel },
            new Human { Id = TeamHarmonyRares, Name = "shared", Type = HumanKind.Webhook });

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
    }

    /// <summary>
    /// With no webhook to prefer, two channels sharing a name leave nothing to choose between. Picking one
    /// would hand over whichever PoracleNG happened to list first, so the grant is dropped.
    /// </summary>
    [Fact]
    public async Task ANameSharedByTwoChannelsResolvesToNeither()
    {
        this.ArrangeDelegate(["shared"]);
        this.ArrangeDirectory(
            new Human { Id = RaidChannel, Name = "shared", Type = HumanKind.DiscordChannel },
            new Human { Id = TelegramChannel, Name = "shared", Type = HumanKind.TelegramChannel });

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    /// <summary>
    /// The directory asks for destination types only, but a server that ignores <c>?type=</c> would hand
    /// back people too. A person is never a delegation target, whatever a grant says.
    /// </summary>
    [Fact]
    public async Task APersonRowFromTheDirectoryIsNeverGranted()
    {
        this.ArrangeDelegate([], localGrants: ["123456789012345678"]);
        this.ArrangeDirectory(new Human { Id = "123456789012345678", Name = "someone", Type = "discord:user" });

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Null(roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }

    [Fact]
    public async Task ADegradedDirectoryIsNotCached()
    {
        this.ArrangeDelegate(["teamharmonyrares"]);
        this._directory.Setup(d => d.GetAsync()).ThrowsAsync(new HttpRequestException("down"));
        var sut = this.CreateSut();

        Assert.False((await sut.ResolveAsync("u1")).Resolved);
        await sut.ResolveAsync("u1");

        this._directory.Verify(d => d.GetAsync(), Times.Exactly(2));
    }

    /// <summary>
    /// The setting is an opt-in, so an unreadable one is the default, off: it must neither widen the grant
    /// nor mark the answer unresolved, which would hold every delegate on the claim during a settings blip.
    /// </summary>
    [Fact]
    public async Task TheSettingFailingToReadCountsAsOff()
    {
        this.ArrangeDelegate([], channels: [RaidChannel], localGrants: [TeamHarmonyRares]);
        this._siteSettings
            .Setup(s => s.GetBoolAsync(UserRoleResolver.PoracleChannelsSettingKey))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.Equal([TeamHarmonyRares], roles.ManagedDestinations);
        Assert.True(roles.Resolved);
    }
}
