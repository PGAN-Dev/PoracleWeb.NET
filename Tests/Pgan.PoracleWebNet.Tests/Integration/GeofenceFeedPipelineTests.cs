using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// Drives <c>POST /api/geofence-feed/refresh</c> and <c>GET /api/geofence-feed</c> through the real
/// ASP.NET Core pipeline -- routing, middleware, global filters and the Angular SPA fallback -- rather
/// than by constructing the controller.
/// </summary>
/// <remarks>
/// The controller-level tests prove the action's logic and nothing about whether a request ever reaches
/// it. #844 reports the opposite symptom: every path on :8082 answers the SPA catch-all, so there is
/// nothing to POST to. That catch-all (<c>MapFallbackToFile("index.html")</c>) is registered only when
/// the environment is NOT Development, so these tests boot the host as Production over a real
/// wwwroot/index.html carrying a marker string. Every assertion that the endpoint was reached is paired
/// with proof that the fallback was live and would otherwise have swallowed the request.
/// </remarks>
public sealed class GeofenceFeedPipelineTests : IDisposable
{
    private const string Secret = "integration-shared-secret";
    private const string SpaMarker = "SPA_FALLBACK_MARKER_DO_NOT_MATCH_ROUTES";
    private const string SecretHeader = "X-Poracle-Secret";
    private const string KojiCacheKey = "koji_admin_geofences";

    private readonly PipelineFactory _factory = new(Secret);

    public void Dispose() => this._factory.Dispose();

    // --------------------------------------------------------------
    // 1. Does the route resolve through the real pipeline?
    // --------------------------------------------------------------

    /// <summary>
    /// The control. If this fails, every other assertion in this file is meaningless, because it would
    /// mean the SPA catch-all is not registered and "the controller answered" proves nothing.
    /// </summary>
    [Theory]
    [InlineData("/api/geofence-feed/not-a-real-action")]
    [InlineData("/api/no-such-controller")]
    [InlineData("/some/spa/route")]
    public async Task TheSpaCatchAllIsLiveAndOwnsEveryUnroutedPath(string path)
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The second half of the control, and the more interesting one. The SPA fallback endpoint accepts
    /// only GET and HEAD, so a POST to a path no controller claims is rejected on method and comes back
    /// 405 -- not 404, and not index.html. That is exactly the sentinel pgan-web#345 reads as "this build
    /// has no refresh endpoint", so it is what a PoracleWeb without this fix answers on the refresh path.
    /// </summary>
    [Theory]
    [InlineData("/api/geofence-feed/not-a-real-action")]
    [InlineData("/api/no-such-controller")]
    [InlineData("/some/spa/route")]
    public async Task PostingToAnUnroutedPathIs405FromTheSpaFallback(string path)
    {
        using var client = this._factory.CreateClient();

        using var response = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.DoesNotContain(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>The endpoint outranks the catch-all: a POST reaches the action, not index.html.</summary>
    [Fact]
    public async Task RefreshReachesTheActionRatherThanTheSpaFallback()
    {
        using var client = this._factory.CreateClient();

        using var response = await client.PostAsync("/api/geofence-feed/refresh", content: null);

        // Refused for want of a secret -- but refused by the controller, which is the point. A build
        // without the action answers 405 here (see PostingToAnUnroutedPathIs405FromTheSpaFallback).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // --------------------------------------------------------------
    // 2. Auth behaviour end to end
    // --------------------------------------------------------------

    /// <summary>
    /// The real proof that the cache is dropped: seed the live IMemoryCache the real KojiService reads,
    /// confirm the feed serves it, POST the refresh, confirm the key is gone and the feed no longer
    /// serves it. No mock of IKojiService is involved.
    /// </summary>
    [Fact]
    public async Task CorrectSecretReturnsOkAndActuallyDropsTheCachedKojiCollection()
    {
        using var client = this._factory.CreateClient();
        var cache = this._factory.Services.GetRequiredService<IMemoryCache>();
        cache.Set(KojiCacheKey, new List<AdminGeofence> { new() { Id = 7, Name = "cached-admin-area" } });

        Assert.Contains("cached-admin-area", await client.GetStringAsync("/api/geofence-feed"), StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());

        Assert.False(cache.TryGetValue(KojiCacheKey, out _));
        Assert.DoesNotContain("cached-admin-area", await client.GetStringAsync("/api/geofence-feed"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every refusal must be 401. The deployed client (pgan-web#345) reads 404/405 as "this build has no
    /// refresh endpoint" and stops trying, so a refusal arriving as either would silently disable it.
    /// </summary>
    /// <remarks>
    /// Deliberately no trailing-whitespace case here, although the controller-level theory has one
    /// (<c>"shared-secret "</c>, asserted to be refused). TestServer hands the header value to the app
    /// verbatim, so that case would pass here for the wrong reason: Kestrel strips optional trailing
    /// whitespace from a field value per RFC 9110 5.5, so over real HTTP the secret arrives trimmed and
    /// is ACCEPTED. Verified with curl against a local Kestrel instance -- 200, not 401. Asserting the
    /// refusal here would enshrine a claim the deployed server does not honour.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-secret")]
    [InlineData("INTEGRATION-SHARED-SECRET")]
    public async Task RefreshRefusesWithUnauthorizedAndNeverWithNotFoundOrMethodNotAllowed(string? supplied)
    {
        using var client = this._factory.CreateClient();
        var cache = this._factory.Services.GetRequiredService<IMemoryCache>();
        cache.Set(KojiCacheKey, new List<AdminGeofence> { new() { Id = 7, Name = "cached-admin-area" } });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        if (supplied is not null)
        {
            request.Headers.TryAddWithoutValidation(SecretHeader, supplied);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(cache.TryGetValue(KojiCacheKey, out _), "a refused request must not drop the cache");
    }

    /// <summary>Fails closed: no configured secret refuses everyone, including an empty header.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    public async Task RefreshFailsClosedThroughThePipelineWhenNoSecretIsConfigured(string? supplied)
    {
        using var factory = new PipelineFactory(configuredSecret: null);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        if (supplied is not null)
        {
            request.Headers.TryAddWithoutValidation(SecretHeader, supplied);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>No JWT is needed: the refusal is the secret check, not the auth middleware challenging.</summary>
    [Fact]
    public async Task RefreshIsNotGatedByTheJwtAuthMiddleware()
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --------------------------------------------------------------
    // 3. The feed itself is unchanged
    // --------------------------------------------------------------

    [Fact]
    public async Task TheFeedIsStillAnonymousAndKeepsItsShape()
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync("/api/geofence-feed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("data").ValueKind);

        var user = root.GetProperty("data").EnumerateArray().Single();
        Assert.Equal("downtown", user.GetProperty("name").GetString());
        Assert.False(user.GetProperty("userSelectable").GetBoolean());
        Assert.False(user.GetProperty("displayInMatches").GetBoolean());
    }

    /// <summary>The feed keeps working after a refresh, rather than being left in a broken state.</summary>
    [Fact]
    public async Task TheFeedStillServesAfterARefresh()
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);
        using var refresh = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        using var response = await client.GetAsync("/api/geofence-feed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }

    // --------------------------------------------------------------
    // 4. Pipeline interactions an internal caller could trip over
    // --------------------------------------------------------------

    /// <summary>
    /// The refresh is throttled per IP. It is an anonymous POST whose only protection is a shared
    /// secret, and fixed-time comparison defends the comparison rather than the volume, so an attacker
    /// guessing the secret was previously bounded only by how fast they could ask.
    /// </summary>
    /// <remarks>
    /// 20 a minute is well above what the caller needs: provisioning fires one refresh per area it
    /// creates. The first 20 must all succeed, which is the half that matters — a limit that throttled
    /// the real caller would be worse than no limit at all.
    /// </remarks>
    [Fact]
    public async Task RefreshIsThrottledButNotBelowWhatTheCallerNeeds()
    {
        using var client = this._factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 25; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
            request.Headers.Add(SecretHeader, Secret);
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(20), status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>
    /// CORS does not block a non-browser caller: an unlisted Origin still executes, it simply gets no
    /// Access-Control-Allow-Origin back. Worth pinning because an internal caller sending an Origin
    /// header would otherwise look like it should be refused.
    /// </summary>
    [Fact]
    public async Task AnUnlistedOriginStillExecutesButGetsNoCorsHeader()
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);
        request.Headers.Add("Origin", "https://not-allowed.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>No antiforgery: a bare POST with no token and no content type is accepted.</summary>
    [Fact]
    public async Task RefreshNeedsNoAntiforgeryTokenOrBody()
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Routing is case-insensitive and tolerates a trailing slash, so neither falls to the SPA.</summary>
    [Theory]
    [InlineData("/API/GEOFENCE-FEED/REFRESH")]
    [InlineData("/api/geofence-feed/refresh/")]
    public async Task RefreshResolvesForCaseAndTrailingSlashVariants(string path)
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(SecretHeader, Secret);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A GET to the refresh path is NOT a 405 -- the SPA catch-all matches every method, so it wins once
    /// the POST-only endpoint is rejected on method. Pinned so nobody diagnoses a misconfigured caller by
    /// GETting the path and concluding the endpoint is missing from the build.
    /// </summary>
    [Fact]
    public async Task GettingTheRefreshPathFallsThroughToTheSpaRatherThanReturning405()
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync("/api/geofence-feed/refresh");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>The security-header middleware runs for this route like any other.</summary>
    [Fact]
    public async Task RefreshResponsesCarryTheSecurityHeaders()
    {
        using var client = this._factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/geofence-feed/refresh");
        request.Headers.Add(SecretHeader, Secret);
        using var response = await client.SendAsync(request);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    // --------------------------------------------------------------
    // 6. Locale bundles must never be served stale (#888)
    // --------------------------------------------------------------

    /// <summary>
    /// ngx-translate fetches <c>./assets/i18n/{lang}.json</c>, a URL built at runtime and carrying no
    /// content hash, unlike every JS bundle beside it. Without a directive the browser keeps whatever
    /// copy it already has across a deploy, and a key added in that deploy renders as the key itself --
    /// so the page reads as broken, and only for the people who were already using it.
    /// </summary>
    [Fact]
    public async Task TranslationBundlesAreServedNoCache()
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync("/assets/i18n/en.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, "locale bundles must revalidate before use");
    }

    /// <summary>
    /// The cost half. "no-cache" is revalidate-before-use, not do-not-store, so an unchanged bundle
    /// still answers 304 with no body -- one conditional request per load, not 150 KB of JSON. This is
    /// the assertion that fails if somebody "hardens" the directive into no-store later.
    /// </summary>
    [Fact]
    public async Task AnUnchangedTranslationBundleRevalidatesTo304()
    {
        using var client = this._factory.CreateClient();

        using var first = await client.GetAsync("/assets/i18n/en.json");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(first.Headers.CacheControl?.NoStore ?? false);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/assets/i18n/en.json");
        conditional.Headers.IfNoneMatch.Add(etag);
        using var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    /// <summary>
    /// The sibling, and the reason the options object is shared. index.html is the other file fetched
    /// by a name that never changes, and it names the hashed bundles -- so a stale one pins a returning
    /// visitor to an entire old build. All three paths are asserted because they do not go through the
    /// same middleware: the fallback endpoint matches <c>/</c> and <c>/dashboard</c>, and
    /// StaticFileMiddleware skips a request that already has an endpoint, so those two never reach
    /// <c>UseDefaultFiles</c> at all. Only <c>/index.html</c> -- which the <c>nonfile</c> constraint
    /// keeps off the fallback -- is served by <c>UseStaticFiles</c>. Configure one and not the other
    /// and the assertion that passes is the one nobody's browser ever requests.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/dashboard")]
    public async Task TheSpaShellIsRevalidatedHoweverItIsReached(string path)
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SpaMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoCache, "the SPA shell must revalidate before use");
    }

    /// <summary>
    /// The half that keeps this scoped to i18n. Help screenshots are large and a stale one is cosmetic,
    /// and the JS bundles are already fingerprinted; widening the rule to all of wwwroot would make
    /// every visitor revalidate the lot on every load to fix a problem neither of them has.
    /// </summary>
    [Theory]
    [InlineData("/assets/help/sidenav.png")]
    [InlineData("/main-ABCD1234.js")]
    public async Task OtherStaticFilesAreLeftAlone(string path)
    {
        using var client = this._factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.CacheControl?.NoCache ?? false);
    }

    /// <summary>
    /// Boots the real Program pipeline as Production, so MapFallbackToFile is registered, over a
    /// throwaway content root holding a marked index.html.
    /// </summary>
    private sealed class PipelineFactory(string? configuredSecret) : WebApplicationFactory<Program>
    {
        // Port 1 refuses instantly, so the startup ALTER TABLE and MigrateAsync fail fast into their
        // existing try/catch instead of waiting out a connect timeout. Nothing under test touches a DB.
        private const string DeadDb = "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none";

        private readonly string _contentRoot = CreateContentRoot();
        private readonly string? _configuredSecret = configuredSecret;

        private static string CreateContentRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "pweb-pipeline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
            File.WriteAllText(
                Path.Combine(root, "wwwroot", "index.html"),
                $"<!doctype html><html><body>{SpaMarker}</body></html>");

            // A locale bundle, plus two files that must be left alone, so the caching assertions have
            // both halves to work with. See #888.
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "assets", "i18n"));
            File.WriteAllText(Path.Combine(root, "wwwroot", "assets", "i18n", "en.json"), """{"NAV":{"DASHBOARD":"Dashboard"}}""");
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "assets", "help"));
            File.WriteAllText(Path.Combine(root, "wwwroot", "assets", "help", "sidenav.png"), "not really a png");
            File.WriteAllText(Path.Combine(root, "wwwroot", "main-ABCD1234.js"), "console.log(0);");

            return root;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseContentRoot(this._contentRoot);
            builder.UseWebRoot(Path.Combine(this._contentRoot, "wwwroot"));

            builder.UseSetting("ConnectionStrings:PoracleDb", DeadDb);
            builder.UseSetting("ConnectionStrings:PoracleWebDb", DeadDb);
            builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-at-least-32-chars");
            builder.UseSetting("Jwt:Issuer", "PoracleWeb");
            builder.UseSetting("Jwt:Audience", "PoracleWeb.App");
            builder.UseSetting("Discord:ClientId", "test-client-id");
            builder.UseSetting("Discord:ClientSecret", "test-client-secret");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://allowed.example");
            builder.UseSetting("Poracle:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Poracle:ApiSecret", this._configuredSecret);
            // Unreachable, so a real cache miss fails fast into the feed's own catch. The cache itself
            // is driven directly from the test, which is what makes the invalidation assertion real.
            builder.UseSetting("Koji:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Koji:ProjectName", "test");

            builder.ConfigureTestServices(services =>
            {
                // Background services are irrelevant to routing and would chatter at unreachable hosts.
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }

                // The only stub: the user half of the feed, which would otherwise need MySQL.
                var repository = new Mock<IUserGeofenceRepository>();
                repository.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(
                [
                    new UserGeofence
                    {
                        Id = 1,
                        KojiName = "downtown",
                        PolygonJson = "[[1.0,2.0],[3.0,4.0],[5.0,6.0]]",
                        Status = "active",
                    },
                ]);

                services.RemoveAll<IUserGeofenceRepository>();
                services.AddScoped(_ => repository.Object);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                try
                {
                    Directory.Delete(this._contentRoot, recursive: true);
                }
                catch (IOException)
                {
                    // A throwaway temp directory; losing the race to delete it does not matter.
                }
            }
        }
    }
}
