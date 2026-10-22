using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The two primitives the rule repair tool stands on: reading every stored rule raw on every profile, and
/// replacing one stored rule on its own profile.
/// </summary>
public class PoracleTrackingProxyRepairTests
{
    private const string ApiAddress = "http://localhost:3030";

    private const string AllProfiles = """
        {"human":{"id":"user1","current_profile_no":2},
         "profiles":[{"uid":1,"id":"user1","profile_no":1,"name":"work"},{"uid":2,"id":"user1","profile_no":2,"name":"home"}],
         "tracking":{"pokemon":[{"uid":5,"profile_no":1,"pokemon_id":25,"min_iv":null,"gender":"female"},{"uid":6,"profile_no":2,"pokemon_id":26}],
                     "incident":[{"uid":289,"profile_no":2,"display_type":8}],"invasion":[{"uid":3,"profile_no":1,"grunt_type":"mixed"}],"raid":null}}
        """;

    private const string RaidRow = """
        {"uid":12,"id":"user1","profile_no":3,"pokemon_id":150,"level":9000,"team":4,"exclusive":0,"form":0,"move":9000,
         "evolution":9000,"gym_id":"","distance":0,"clean":0,"template":"1","rsvp_changes":0,"ping":"","description":"x"}
        """;

    [Fact]
    public async Task StoredRulesIncludeEveryProfileRaw()
    {
        var handler = new Router().On("/tracking", AllProfiles);

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        // Descriptions are never used here, so PoracleNG is not asked to render them for every rule.
        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking?all_profiles=true", sent.Url);

        var pokemon = rules.Where(r => r.Type == "pokemon").ToList();
        Assert.Equal([(1, 5), (2, 6)], pokemon.Select(r => (r.ProfileNo, r.Uid)));
        using var doc = JsonDocument.Parse(AllProfiles);
        var original = doc.RootElement.GetProperty("tracking").GetProperty("pokemon")[0];
        Assert.Equal(original.GetRawText(), pokemon[0].Rule.GetRawText());
        Assert.Equal(JsonValueKind.Null, pokemon[0].Rule.GetProperty("min_iv").ValueKind);
        Assert.Equal("female", pokemon[0].Rule.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task StoredRulesKeepIncidentSeparate()
    {
        var rules = await CreateSut(new Router().On("/tracking", AllProfiles)).GetStoredRulesAsync("user1");

        var incident = Assert.Single(rules, r => r.Type == "incident");
        Assert.Equal((2, 289), (incident.ProfileNo, incident.Uid));
        Assert.Equal(8, incident.Rule.GetProperty("display_type").GetInt32());

        var invasion = Assert.Single(rules, r => r.Type == "invasion");
        Assert.Equal(3, invasion.Uid);
        Assert.DoesNotContain(rules, r => r.Type == "raid");
    }

    [Fact]
    public async Task StoredRulesAppendActiveProfileWithoutRow()
    {
        // Routes are matched newest first, so the all_profiles route is added last to win over "/tracking".
        var handler = new Router()
            .On("/tracking", """
                {"human":{"id":"user1","current_profile_no":1},"profiles":[],
                 "tracking":{"pokemon":[{"uid":5,"pokemon_id":25}],"incident":[{"uid":9,"display_type":7}]}}
                """)
            .On("all_profiles=true", """{"human":{"id":"user1","current_profile_no":1},"profiles":[],"tracking":{"pokemon":[]}}""");

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        Assert.Equal(
            [$"{ApiAddress}/api/v2/humans/user1/tracking?all_profiles=true",
             $"{ApiAddress}/api/v2/humans/user1/tracking"],
            handler.Requests.Select(r => r.Url));
        Assert.Equal([("pokemon", 1, 5), ("incident", 1, 9)], rules.Select(r => (r.Type, r.ProfileNo, r.Uid)));
    }

    [Fact]
    public async Task StoredRulesDoNotDuplicateActiveProfileRulesTheSnapshotAlreadyCarries()
    {
        // jfberry/PoracleNG#331: all_profiles also returns rules on a profile with no profiles row, the active one
        // included, each carrying profile_no. The second read still runs for servers without it, and must not add
        // the same rules again. uids are per table, so incident 5 and pokemon 5 are different rules.
        var handler = new Router()
            .On("/tracking", """
                {"human":{"id":"user1","current_profile_no":1},"profiles":[],
                 "tracking":{"pokemon":[{"uid":5,"pokemon_id":25}],"incident":[{"uid":5,"display_type":7}]}}
                """)
            .On("all_profiles=true", """
                {"human":{"id":"user1","current_profile_no":1},"profiles":[],
                 "tracking":{"pokemon":[{"uid":5,"profile_no":1,"pokemon_id":25}],"incident":[{"uid":5,"profile_no":1,"display_type":7}]}}
                """);

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([("pokemon", 1, 5), ("incident", 1, 5)], rules.Select(r => (r.Type, r.ProfileNo, r.Uid)));
    }

    [Fact]
    public async Task StoredRulesReadNothingMoreWhenTheActiveProfileIsListedOnlyByTheSecondRead()
    {
        // The first snapshot names an active profile with no row; by the second read the scheduler has moved the
        // human to one that has a row, whose rules the first snapshot already carried.
        var handler = new Router()
            .On("/tracking", """
                {"human":{"id":"user1","current_profile_no":2},"profiles":[],
                 "tracking":{"pokemon":[{"uid":6,"pokemon_id":26}]}}
                """)
            .On("all_profiles=true", """
                {"human":{"id":"user1","current_profile_no":3},"profiles":[{"profile_no":2}],
                 "tracking":{"pokemon":[{"uid":6,"profile_no":2,"pokemon_id":26}]}}
                """);

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([("pokemon", 2, 6)], rules.Select(r => (r.Type, r.ProfileNo, r.Uid)));
    }

    [Fact]
    public async Task StoredRulesDoNotDuplicateAProfileTheListNames()
    {
        var handler = new Router().On("/tracking", AllProfiles);

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        Assert.Single(handler.Requests);
        Assert.Equal(2, rules.Count(r => r.Type == "pokemon"));
    }

    [Fact]
    public async Task ReplaceStoredRuleTargetsItsProfileAndIsTrusted()
    {
        var handler = new Router().On("/tracking/raid/12", """{"dry_run":true,"created":null,"updated":[{"uid":12}],"unchanged":null}""");

        await CreateSut(handler).ReplaceStoredRuleAsync("raid", "user1", 12, 3, Row(RaidRow), dryRun: true);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/raid/12?silent=true&trusted=true&profile=3&dry_run=true", sent.Url);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.False(body.RootElement.TryGetProperty("profile_no", out _));
        Assert.False(body.RootElement.TryGetProperty("uid", out _));
        Assert.Equal(150, body.RootElement.GetProperty("pokemon_id").GetInt32());
    }

    [Fact]
    public async Task ReplaceStoredRuleOmitsDryRunWhenApplying()
    {
        var handler = new Router().On("/tracking/raid/12", """{"created":null,"updated":[{"uid":12}],"unchanged":null}""");

        var outcome = await CreateSut(handler).ReplaceStoredRuleAsync("raid", "user1", 12, 3, Row(RaidRow), dryRun: false);

        Assert.Equal(StoredRuleReplaceOutcome.Updated, outcome);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking/raid/12?silent=true&trusted=true&profile=3", handler.Requests[0].Url);
    }

    [Fact]
    public async Task ReplaceStoredRuleMapsOutcomes()
    {
        Task<StoredRuleReplaceOutcome> Run(HttpStatusCode status, string body, string type = "application/json") =>
            CreateSut(new Router().On("/tracking/raid/12", body, status, type))
                .ReplaceStoredRuleAsync("raid", "user1", 12, 3, Row(RaidRow), dryRun: true);

        Assert.Equal(StoredRuleReplaceOutcome.Updated, await Run(HttpStatusCode.OK, """{"updated":[{"uid":12}],"unchanged":null}"""));
        Assert.Equal(StoredRuleReplaceOutcome.Unchanged, await Run(HttpStatusCode.OK, """{"updated":null,"unchanged":[{"uid":12}]}"""));
        Assert.Equal(
            StoredRuleReplaceOutcome.Conflict,
            await Run(HttpStatusCode.Conflict, """{"title":"Conflict","status":409,"detail":"identical"}""", "application/problem+json"));
        Assert.Equal(
            StoredRuleReplaceOutcome.NotFound,
            await Run(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"raid rule 12 not found"}""", "application/problem+json"));

        await Assert.ThrowsAsync<AccountGoneException>(
            () => Run(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""", "application/problem+json"));
        await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => Run(HttpStatusCode.NotFound, "404 page not found", "text/plain"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"updated":null,"unchanged":null}""")]
    [InlineData("""{"created":[{"uid":0}],"updated":null,"unchanged":null}""")]
    [InlineData("""{"created":[{"uid":0}],"unchanged":[{"uid":12}]}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task ReplaceStoredRuleNeverTreatsAnAmbiguousAnswerAsUpdated(string answer)
    {
        var handler = new Router().On("/tracking/raid/12", answer);

        var ex = await Assert.ThrowsAsync<PoracleAmbiguousAnswerException>(
            () => CreateSut(handler).ReplaceStoredRuleAsync("raid", "user1", 12, 3, Row(RaidRow), dryRun: true));

        Assert.Contains("raid rule 12", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredRulesSkipAnAllProfilesRuleWithNoProfileNo()
    {
        var handler = new Router().On("/tracking", """
            {"human":{"current_profile_no":1},"profiles":[{"profile_no":1}],
             "tracking":{"pokemon":[{"uid":5,"pokemon_id":25},{"uid":6,"profile_no":1,"pokemon_id":26}]}}
            """);

        var rules = await CreateSut(handler).GetStoredRulesAsync("user1");

        Assert.Equal(6, Assert.Single(rules).Uid);
    }

    [Fact]
    public async Task ReplaceStoredRuleRefusesAnUntranslatableRowBeforeAnyRequest()
    {
        var handler = new Router();

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(
            () => CreateSut(handler).ReplaceStoredRuleAsync("raid", "user1", 12, 3, Row("""{"uid":12,"some_field_from_a_newer_poracle":4}"""), dryRun: true));

        Assert.StartsWith("PoracleNG can't store this alarm as it is: ", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReplaceStoredRuleNeverAsksThePolicy()
    {
        var policy = new Mock<IUserAreaPolicy>(MockBehavior.Strict);
        var handler = new Router().On("/tracking/raid/12", """{"updated":[{"uid":12}]}""");
        var row = Row("""
            {"uid":12,"id":"user1","pokemon_id":150,"level":9000,"team":4,"exclusive":0,"form":0,"move":9000,"evolution":9000,
             "gym_id":"","distance":0,"clean":0,"template":"1","rsvp_changes":0,"override_areas":["terrigal"]}
            """);

        await CreateSut(handler, policy.Object).ReplaceStoredRuleAsync("raid", "user1", 12, 3, row, dryRun: false);

        policy.VerifyNoOtherCalls();
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("terrigal", body.RootElement.GetProperty("override_areas")[0].GetString());
    }

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static PoracleTrackingProxy CreateSut(HttpMessageHandler handler, IUserAreaPolicy? policy = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build();

        return new PoracleTrackingProxy(
            new HttpClient(handler),
            config,
            policy ?? PoracleTrackingProxyTests.AnyAreaPolicy(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    private sealed class Router : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body, HttpStatusCode Status, string ContentType)> _routes = [];

        public List<Sent> Requests { get; } = [];

        public Router On(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
        {
            this._routes.Insert(0, (fragment, body, status, contentType));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (request.RequestUri.AbsolutePath.EndsWith("/openapi.json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"components":{"schemas":{}}}""", Encoding.UTF8, "application/json"),
                };
            }

            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(new Sent(request.Method, url, body));

            foreach (var (fragment, text, status, contentType) in this._routes)
            {
                if (url.Contains(fragment, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, contentType) };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("404 page not found", Encoding.UTF8, "text/plain"),
            };
        }
    }
}
