using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Every consumer that used to see Pokestop events (kecleon, gold-stop, showcase) in the invasion list,
/// driven through the real <see cref="PoracleTrackingProxy"/> over a scripted PoracleNG 5.4.0.
/// </summary>
/// <remarks>
/// v2 keeps those rules in <c>incident</c> and its invasion routes leave them out; the proxy folds them back
/// into the invasion list in the v1 row form. These tests pin that each consumer still gets the result it did
/// on v1, with an event rule present in the v2 data. The mocked-proxy tests of each service cannot show that:
/// they hand the service a list that already contains the event.
/// </remarks>
public class PokestopEventConsumerTests
{
    private const string Api = "http://localhost:3030";

    private const string InvasionRules = """{"rules":[{"uid":1,"grunt_type":"water","gender":"male","distance":0,"clean":false}]}""";

    private const string IncidentRules = """{"rules":[{"uid":289,"display_type":8,"distance":0,"clean":false},{"uid":290,"display_type":9,"distance":0,"clean":false}]}""";

    private const string Snapshot = """
        {"human":{"id":"u1","current_profile_no":1},"profiles":[{"uid":1,"id":"u1","profile_no":1,"name":"x","area":[],"latitude":0,"longitude":0}],
         "tracking":{"invasion":[{"uid":1,"grunt_type":"water","gender":"male","clean":false}],
                     "incident":[{"uid":289,"display_type":8,"clean":true},{"uid":290,"display_type":9,"clean":true}]}}
        """;

    private readonly Mock<IFeatureGate> _gate = new();

    public PokestopEventConsumerTests()
    {
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
    }

    private void EventsPageOff() =>
        this._gate.Setup(g => g.IsEnabledAsync(DisableFeatureKeys.PokestopEvents)).ReturnsAsync(false);

    // -- DashboardService -------------------------------------------------

    [Fact]
    public async Task TheDashboardCountsEventsUnderPokestopEventsWhenThatPageExists()
    {
        var proxy = CreateProxy(new Handler().Get("/tracking", Snapshot));

        var counts = await new DashboardService(proxy, this._gate.Object).GetCountsAsync("u1", 1);

        Assert.Equal(1, counts.Invasions);
        Assert.Equal(2, counts.PokestopEvents);
    }

    [Fact]
    public async Task TheDashboardCountsEventsUnderInvasionsWhenTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var proxy = CreateProxy(new Handler().Get("/tracking", Snapshot));

        var counts = await new DashboardService(proxy, this._gate.Object).GetCountsAsync("u1", 1);

        Assert.Equal(3, counts.Invasions);
        Assert.Equal(0, counts.PokestopEvents);
    }

    // -- InvasionService --------------------------------------------------

    [Fact]
    public async Task TheInvasionListHidesEventsWhenThePokestopEventsPageIsOn()
    {
        var sut = this.CreateInvasions(new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules));

        var list = (await sut.GetByUserAsync("u1", 1)).ToList();

        Assert.Equal([1], list.Select(i => i.Uid));
        Assert.Null(await sut.GetByUidAsync("u1", 289));
    }

    [Fact]
    public async Task TheInvasionListKeepsEventsWhenTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var sut = this.CreateInvasions(new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules));

        var list = (await sut.GetByUserAsync("u1", 1)).ToList();

        Assert.Equal([1, 289, 290], list.Select(i => i.Uid));
        Assert.Equal(["water", "kecleon", "showcase"], list.Select(i => i.GruntType));
    }

    [Fact]
    public async Task InvasionDeleteAllLeavesEventsToTheirOwnPage()
    {
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Delete("/tracking/invasion", """{"deleted":[{"uid":1}]}""");

        var count = await this.CreateInvasions(handler).DeleteAllByUserAsync("u1", 1);

        Assert.Equal(1, count);
        var delete = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("""{"uids":[1]}""", delete.Body);
    }

    [Fact]
    public async Task InvasionDeleteAllRemovesEventsWhenTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Delete("/tracking/invasion", """{"deleted":[{"uid":1}]}""")
            .Delete("/tracking/incident", """{"deleted":[{"uid":289},{"uid":290}]}""");

        var count = await this.CreateInvasions(handler).DeleteAllByUserAsync("u1", 1);

        Assert.Equal(3, count);
        var deletes = handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        Assert.Contains("/tracking/incident", deletes[^1].Url, StringComparison.Ordinal);
        Assert.Equal("""{"uids":[289,290]}""", deletes[^1].Body);
    }

    [Fact]
    public async Task DeletingASingleEventUidThroughTheInvasionServiceReachesIncident()
    {
        // Quick-pick Remove and the invasion page both call this for a uid they read from the list.
        this.EventsPageOff();
        var handler = new Handler()
            .Delete("/tracking/invasion/289", """{"title":"Not Found","status":404,"detail":"rule not found"}""", HttpStatusCode.NotFound, "application/problem+json")
            .Delete("/tracking/incident/289", """{"deleted":[{"uid":289}]}""");

        await this.CreateInvasions(handler).DeleteAsync("u1", 289);

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Delete && r.Url.Contains("/tracking/incident/289", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvasionBulkDistanceWritesOnlyTheInvasionsWhenTheEventsPageIsOn()
    {
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Post("/api/tracking/invasion/u1", """{"newUids":[],"alreadyPresent":0,"updates":1,"insert":0}""");

        await this.CreateInvasions(handler).UpdateDistanceByUserAsync("u1", 1, 500);

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.DoesNotContain("kecleon", post.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("showcase", post.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvasionBulkDistanceStillWritesEventRowsThroughV1WhenTheEventsPageIsOff()
    {
        // 3b/3c: this write path is still v1 (the invasion table). The folded event row has to go back in the
        // shape v1's create accepts: grunt_type as the event name, gender 0.
        this.EventsPageOff();
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Post("/api/tracking/invasion/u1", """{"newUids":[],"alreadyPresent":0,"updates":3,"insert":0}""");

        var result = await this.CreateInvasions(handler).UpdateDistanceByUserAsync("u1", 1, 500);

        Assert.Equal(3, result.Updated);
        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        using var doc = JsonDocument.Parse(post.Body!);
        var rows = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(["water", "kecleon", "showcase"], rows.Select(r => r.GetProperty("grunt_type").GetString()));
        Assert.All(rows, r => Assert.Equal(500, r.GetProperty("distance").GetInt32()));
        Assert.Equal(0, rows[1].GetProperty("gender").GetInt32());
    }

    // -- CleaningService --------------------------------------------------

    [Fact]
    public async Task TheCleaningStatusIgnoresEventsWhenThePokestopEventsPageIsOn()
    {
        // The two event rules in Snapshot are clean, the invasion is not: the invasions switch reports the
        // invasion alone.
        var sut = this.CreateCleaning(new Handler().Get("/tracking", Snapshot));

        var status = await sut.GetCleanStatusAsync("u1", 1);

        Assert.False(status["invasions"]);
    }

    [Fact]
    public async Task TheCleaningStatusCountsEventsWhenTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var snapshot = Snapshot.Replace("\"gender\":\"male\",\"clean\":false", "\"gender\":\"male\",\"clean\":true", StringComparison.Ordinal);
        var sut = this.CreateCleaning(new Handler().Get("/tracking", snapshot));

        var status = await sut.GetCleanStatusAsync("u1", 1);

        Assert.True(status["invasions"]);
    }

    [Fact]
    public async Task TheCleaningStatusIsFalseWhenAnEventIsNotCleanAndTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var snapshot = Snapshot.Replace("\"display_type\":9,\"clean\":true", "\"display_type\":9,\"clean\":false", StringComparison.Ordinal)
            .Replace("\"gender\":\"male\",\"clean\":false", "\"gender\":\"male\",\"clean\":true", StringComparison.Ordinal);
        var sut = this.CreateCleaning(new Handler().Get("/tracking", snapshot));

        var status = await sut.GetCleanStatusAsync("u1", 1);

        Assert.False(status["invasions"]);
    }

    [Fact]
    public async Task TheInvasionCleanToggleSkipsEventsWhenThePokestopEventsPageIsOn()
    {
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Post("/api/tracking/invasion/u1", """{"newUids":[],"alreadyPresent":0,"updates":1,"insert":0}""");

        var count = await this.CreateCleaning(handler).ToggleCleanInvasionsAsync("u1", 1, 1);

        Assert.Equal(1, count);
        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.DoesNotContain("kecleon", post.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInvasionCleanToggleCoversEventsWhenTheEventsPageIsOff()
    {
        this.EventsPageOff();
        var handler = new Handler().Get("/tracking/invasion", InvasionRules).Get("/tracking/incident", IncidentRules)
            .Post("/api/tracking/invasion/u1", """{"newUids":[],"alreadyPresent":0,"updates":3,"insert":0}""");

        var count = await this.CreateCleaning(handler).ToggleCleanInvasionsAsync("u1", 1, 1);

        Assert.Equal(3, count);
        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        using var doc = JsonDocument.Parse(post.Body!);
        var rows = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(["water", "kecleon", "showcase"], rows.Select(r => r.GetProperty("grunt_type").GetString()));
        Assert.All(rows, r => Assert.Equal(1, r.GetProperty("clean").GetInt32()));
    }

    // -- HumanService -----------------------------------------------------
    // Delete-all removing event rules is pinned end to end, over the same real proxy, by
    // PoracleTrackingProxyV2Tests.DeletingEveryAlarmOnAnAccountRemovesItsPokestopEventsToo.

    // -- PokestopEventService / QuickPickService --------------------------
    // PokestopEventService reads and deletes through IPoracleIncidentProxy (incident only), so the fold does
    // not reach it. QuickPickService reads invasion uids through InvasionService.GetByUserAsync (tested above)
    // and removes through InvasionService.DeleteAsync (single event uid tested above).

    private InvasionService CreateInvasions(Handler handler) =>
        new(CreateProxy(handler), this._gate.Object, NullLogger<InvasionService>.Instance, Mock.Of<ITrackedUidRemapper>());

    private CleaningService CreateCleaning(Handler handler) =>
        new(CreateProxy(handler), this._gate.Object, Mock.Of<ITrackedUidRemapper>(), NullLogger<CleaningService>.Instance);

    private static PoracleTrackingProxy CreateProxy(Handler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = Api,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build();

        return new PoracleTrackingProxy(
            new HttpClient(handler),
            config,
            PoracleTrackingProxyTests.AnyAreaPolicy(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    private sealed class Handler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Fragment, string Body, HttpStatusCode Status, string ContentType)> _routes = [];

        public List<Sent> Requests { get; } = [];

        public Handler Get(string fragment, string body) => this.On(HttpMethod.Get, fragment, body);

        public Handler Post(string fragment, string body) => this.On(HttpMethod.Post, fragment, body);

        public Handler Delete(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json") =>
            this.On(HttpMethod.Delete, fragment, body, status, contentType);

        private Handler On(HttpMethod method, string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
        {
            // Later registrations win, so a specific fragment can follow a general one.
            this._routes.Insert(0, (method, fragment, body, status, contentType));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(new Sent(request.Method, url, body));

            foreach (var (method, fragment, reply, status, contentType) in this._routes)
            {
                if (method == request.Method && url.Contains(fragment, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, contentType) };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("404 page not found", Encoding.UTF8, "text/plain"),
            };
        }
    }
}
