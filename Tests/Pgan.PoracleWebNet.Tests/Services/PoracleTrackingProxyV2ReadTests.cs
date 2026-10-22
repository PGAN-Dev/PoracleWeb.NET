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
/// Tracking reads over PoracleNG's <c>/api/v2</c> surface, restated in the v1 shape every caller was
/// written against. Response shapes were taken from a live 5.4.0 instance (2026-10-22).
/// </summary>
public class PoracleTrackingProxyV2ReadTests
{
    private const string ApiAddress = "http://localhost:3030";

    private const string HumanNotFound = """{"title":"Not Found","status":404,"detail":"human not found"}""";

    // ── GetByUserAsync ───────────────────────────────────────────

    [Fact]
    public async Task AListReadRequestsOnlyItsV2Url()
    {
        var handler = new Router().On("/tracking/raid", """{"rules":[]}""");

        await CreateSut(handler).GetByUserAsync("raid", "user42");

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user42/tracking/raid?include_descriptions=true", sent.Url);
    }

    [Fact]
    public async Task AListReadAnswersTheV1RowShape()
    {
        var handler = new Router().On("/tracking/pokemon", """
            {"rules":[{"uid":5,"pokemon_id":25,"gender":"female","clean":true,"edit":true,"min_iv":null,
                       "template":"1","description":"**Pikachu**","personal_shiny":true}]}
            """);

        var rows = await CreateSut(handler).GetByUserAsync("pokemon", "user1");

        var row = Assert.Single(rows.EnumerateArray());
        Assert.Equal(5, row.GetProperty("uid").GetInt32());
        Assert.Equal("user1", row.GetProperty("id").GetString());
        Assert.Equal(2, row.GetProperty("gender").GetInt32());
        Assert.Equal(3, row.GetProperty("clean").GetInt32());
        Assert.Equal(-1, row.GetProperty("min_iv").GetInt32());
        Assert.Equal(100, row.GetProperty("max_iv").GetInt32());
        Assert.Equal("**Pikachu**", row.GetProperty("description").GetString());
        Assert.True(row.GetProperty("personal_shiny").GetBoolean());
        Assert.False(row.TryGetProperty("edit", out _));
    }

    [Theory]
    [InlineData("""{"rules":[]}""")]
    [InlineData("""{"rules":null}""")]
    [InlineData("""{}""")]
    public async Task AnEmptyListIsAnEmptyArray(string body)
    {
        var rows = await CreateSut(new Router().On("/tracking/lure", body)).GetByUserAsync("lure", "user1");

        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(0, rows.GetArrayLength());
    }

    [Fact]
    public async Task InvasionsIncludePokestopEventsInTheirV1Form()
    {
        var handler = new Router()
            .On("/tracking/invasion", """{"rules":[{"uid":1,"gender":"male","grunt_type":"water","clean":false}]}""")
            .On("/tracking/incident", """{"rules":[{"uid":289,"display_type":8,"clean":true},{"uid":290,"display_type":7},{"uid":291,"display_type":9}]}""");

        var rows = await CreateSut(handler).GetByUserAsync("invasion", "user1");

        Assert.Equal(2, handler.Requests.Count);
        var list = rows.EnumerateArray().ToList();
        Assert.Equal(["water", "kecleon", "gold-stop", "showcase"], list.Select(r => r.GetProperty("grunt_type").GetString()));
        Assert.Equal([1, 289, 290, 291], list.Select(r => r.GetProperty("uid").GetInt32()));
        Assert.Equal(1, list[0].GetProperty("gender").GetInt32());
        Assert.Equal(1, list[1].GetProperty("clean").GetInt32());
        Assert.Equal(0, list[1].GetProperty("gender").GetInt32());
        Assert.All(list, r => Assert.False(r.TryGetProperty("display_type", out _)));
    }

    [Fact]
    public async Task APokestopEventOfAnUnknownKindIsLeftOutOfTheInvasionList()
    {
        var logger = new CapturingLogger();
        var handler = new Router()
            .On("/tracking/invasion", """{"rules":[]}""")
            .On("/tracking/incident", """{"rules":[{"uid":1,"display_type":8},{"uid":2,"display_type":99}]}""");

        var rows = await CreateSut(handler, logger).GetByUserAsync("invasion", "user1");

        Assert.Equal([1], rows.EnumerateArray().Select(r => r.GetProperty("uid").GetInt32()));
        Assert.Contains(logger.Warnings, w => w.Contains("99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OneRuleThatCannotBeTranslatedDoesNotHideTheOthers()
    {
        var logger = new CapturingLogger();
        var handler = new Router().On("/tracking/pokemon", """
            {"rules":[{"uid":1,"gender":"any"},{"uid":2,"gender":"robot"},{"uid":3,"gender":"male"}]}
            """);

        var rows = await CreateSut(handler, logger).GetByUserAsync("pokemon", "user1");

        Assert.Equal([1, 3], rows.EnumerateArray().Select(r => r.GetProperty("uid").GetInt32()));
        Assert.Contains(logger.Warnings, w => w.Contains("pokemon", StringComparison.Ordinal) && w.Contains('2'));
    }

    [Fact]
    public async Task AListReadForAMissingHumanIsAGoneAccount() =>
        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(new Router().On("/tracking/pokemon", HumanNotFound, HttpStatusCode.NotFound, "application/problem+json"))
                .GetByUserAsync("pokemon", "user1"));

    [Fact]
    public async Task AListReadOnAServerWithoutTheRouteIsUnsupported()
    {
        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(new Router().On("/tracking/pokemon", "404 page not found", HttpStatusCode.NotFound, "text/plain"))
                .GetByUserAsync("pokemon", "user1"));

        Assert.Equal("Alarm list", ex.Feature);
        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task AListReadThatFailsOtherwiseThrows() =>
        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateSut(new Router().On("/tracking/pokemon", "{}", HttpStatusCode.InternalServerError))
                .GetByUserAsync("pokemon", "user1"));

    // ── GetAllTrackingAsync ──────────────────────────────────────

    private const string Snapshot = """
        {"human":{"id":"user1","current_profile_no":2},"locations":[],"mutes":[],"profiles":[{"uid":1,"id":"user1","profile_no":2,"name":"x","area":["a"],"latitude":1,"longitude":2,"active_hours":[]}],"summaries":[],
         "tracking":{"egg":[],"fort":null,"gym":null,
           "incident":[{"uid":289,"display_type":8,"clean":true}],
           "invasion":[{"uid":1,"grunt_type":"water","gender":"female"}],
           "lure":[{"uid":3,"lure_id":501}],"maxbattle":null,"nest":null,
           "pokemon":[{"uid":5,"pokemon_id":25,"gender":"male","clean":true}],"quest":null,"raid":[{"uid":6,"pokemon_id":null,"level":5,"team":"valor"}]}}
        """;

    [Fact]
    public async Task TheSnapshotRequestsOnlyItsV2Url()
    {
        var handler = new Router().On("/tracking", Snapshot);

        await CreateSut(handler).GetAllTrackingAsync("user99");

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user99/tracking", sent.Url);
    }

    [Fact]
    public async Task TheSnapshotIsKeyedAndShapedLikeV1()
    {
        var all = await CreateSut(new Router().On("/tracking", Snapshot)).GetAllTrackingAsync("user1");

        foreach (var key in new[] { "egg", "fort", "gym", "invasion", "lure", "maxbattle", "nest", "pokemon", "quest", "raid" })
        {
            Assert.Equal(JsonValueKind.Array, all.GetProperty(key).ValueKind);
        }

        Assert.False(all.TryGetProperty("incident", out _));
        Assert.Equal(0, all.GetProperty("quest").GetArrayLength());
        Assert.Equal(0, all.GetProperty("egg").GetArrayLength());

        var pokemon = all.GetProperty("pokemon")[0];
        Assert.Equal(1, pokemon.GetProperty("gender").GetInt32());
        Assert.Equal(1, pokemon.GetProperty("clean").GetInt32());
        Assert.Equal("user1", pokemon.GetProperty("id").GetString());

        var raid = all.GetProperty("raid")[0];
        Assert.Equal(2, raid.GetProperty("team").GetInt32());
        Assert.Equal(5, raid.GetProperty("level").GetInt32());

        var invasions = all.GetProperty("invasion").EnumerateArray().ToList();
        Assert.Equal(["water", "kecleon"], invasions.Select(r => r.GetProperty("grunt_type").GetString()));
    }

    [Fact]
    public async Task ASnapshotTypeThatIsNullOrAbsentIsAnEmptyArray()
    {
        var all = await CreateSut(new Router().On("/tracking", """{"tracking":{"pokemon":null}}""")).GetAllTrackingAsync("user1");

        Assert.Equal(0, all.GetProperty("pokemon").GetArrayLength());
        Assert.Equal(0, all.GetProperty("invasion").GetArrayLength());
    }

    [Fact]
    public async Task ASnapshotRuleThatCannotBeTranslatedIsSkippedAndTheRestKept()
    {
        var logger = new CapturingLogger();
        var all = await CreateSut(
            new Router().On("/tracking", """{"tracking":{"pokemon":[{"uid":1,"gender":"robot"},{"uid":2}]}}"""), logger)
            .GetAllTrackingAsync("user1");

        Assert.Equal([2], all.GetProperty("pokemon").EnumerateArray().Select(r => r.GetProperty("uid").GetInt32()));
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task TheSnapshotForAMissingHumanIsAGoneAccount() =>
        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(new Router().On("/tracking", HumanNotFound, HttpStatusCode.NotFound, "application/problem+json"))
                .GetAllTrackingAsync("user1"));

    [Fact]
    public async Task TheSnapshotOnAServerWithoutTheRouteIsUnsupported()
    {
        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(new Router().On("/tracking", "404 page not found", HttpStatusCode.NotFound, "text/plain"))
                .GetAllTrackingAsync("user1"));

        Assert.Equal("All alarms", ex.Feature);
    }

    // ── GetAllTrackingAllProfilesAsync ───────────────────────────

    private const string AllProfiles = """
        {"human":{"id":"user1","current_profile_no":2},
         "profiles":[{"uid":1,"id":"user1","profile_no":1,"name":"work","area":["a b"],"latitude":1,"longitude":2,"active_hours":[{"day":1,"hours":9,"mins":0}]},
                     {"uid":2,"id":"user1","profile_no":2,"name":"home","area":[],"latitude":3,"longitude":4,"active_hours":[]}],
         "tracking":{"pokemon":[{"uid":5,"profile_no":1,"pokemon_id":25,"description":"d1"},{"uid":6,"profile_no":2,"pokemon_id":26}],
                     "incident":[{"uid":289,"profile_no":2,"display_type":8,"description":"ev"}],"invasion":null}}
        """;

    [Fact]
    public async Task TheAllProfilesReadRequestsOnlyItsV2Url()
    {
        var handler = new Router().On("/tracking", AllProfiles);

        await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{ApiAddress}/api/v2/humans/user1/tracking?all_profiles=true&include_descriptions=true", sent.Url);
    }

    [Fact]
    public async Task TheAllProfilesReadKeepsProfileNoAndDescriptionPerRule()
    {
        var all = await CreateSut(new Router().On("/tracking", AllProfiles)).GetAllTrackingAllProfilesAsync("user1");

        var pokemon = all.GetProperty("pokemon").EnumerateArray().ToList();
        Assert.Equal([1, 2], pokemon.Select(r => r.GetProperty("profile_no").GetInt32()));
        Assert.Equal("d1", pokemon[0].GetProperty("description").GetString());

        var evt = all.GetProperty("invasion")[0];
        Assert.Equal("kecleon", evt.GetProperty("grunt_type").GetString());
        Assert.Equal(2, evt.GetProperty("profile_no").GetInt32());
        Assert.Equal("ev", evt.GetProperty("description").GetString());
    }

    [Fact]
    public async Task TheAllProfilesReadCarriesTheProfilesTheOverviewPageReads()
    {
        var all = await CreateSut(new Router().On("/tracking", AllProfiles)).GetAllTrackingAllProfilesAsync("user1");

        var profiles = all.GetProperty("profile").EnumerateArray().ToList();
        Assert.Equal(2, profiles.Count);
        Assert.Equal("work", profiles[0].GetProperty("name").GetString());
        // v1 stored both as JSON text.
        Assert.Equal(JsonValueKind.String, profiles[0].GetProperty("area").ValueKind);
        Assert.Equal("""["a b"]""", profiles[0].GetProperty("area").GetString());
        Assert.Equal(JsonValueKind.String, profiles[0].GetProperty("active_hours").ValueKind);
        Assert.Equal("ok", all.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AHumanWithNoProfileRowsFallsBackToTheActiveProfileSnapshot()
    {
        // PoracleNG answers ?all_profiles=true with nothing for a human who has no profiles rows, though
        // the rules exist (and v1's allProfiles returned them).
        var handler = new Router()
            .On("all_profiles=true", """{"human":{"id":"user1","current_profile_no":1},"profiles":[],"tracking":{"pokemon":[]}}""")
            .On("/tracking?include_descriptions=true", """
                {"human":{"id":"user1","current_profile_no":1},"profiles":[],
                 "tracking":{"pokemon":[{"uid":5,"pokemon_id":25,"description":"d"}],"incident":[{"uid":9,"display_type":7}]}}
                """);

        var all = await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        Assert.Equal(
            [$"{ApiAddress}/api/v2/humans/user1/tracking?all_profiles=true&include_descriptions=true",
             $"{ApiAddress}/api/v2/humans/user1/tracking?include_descriptions=true"],
            handler.Requests.Select(r => r.Url));
        var rule = Assert.Single(all.GetProperty("pokemon").EnumerateArray());
        Assert.Equal(5, rule.GetProperty("uid").GetInt32());
        Assert.Equal(1, rule.GetProperty("profile_no").GetInt32());
        Assert.Equal("d", rule.GetProperty("description").GetString());
        Assert.Equal(1, all.GetProperty("invasion")[0].GetProperty("profile_no").GetInt32());
        Assert.Equal(0, all.GetProperty("profile").GetArrayLength());
    }

    [Fact]
    public async Task TheFallbackStampsTheHumansCurrentProfileNotAConstant()
    {
        var handler = new Router()
            .On("all_profiles=true", """{"human":{"current_profile_no":3},"profiles":null,"tracking":null}""")
            .On("/tracking?include_descriptions=true", """{"human":{"current_profile_no":3},"tracking":{"lure":[{"uid":1,"lure_id":501}]}}""");

        var all = await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        Assert.Equal(3, all.GetProperty("lure")[0].GetProperty("profile_no").GetInt32());
    }

    [Fact]
    public async Task AnActiveProfileWithNoRowIsAppendedToTheListedProfiles()
    {
        var handler = new Router()
            .On("all_profiles=true", """
                {"human":{"current_profile_no":1},
                 "profiles":[{"uid":2,"id":"user1","profile_no":2,"name":"b","area":[],"latitude":0,"longitude":0,"active_hours":[]}],
                 "tracking":{"pokemon":[{"uid":6,"profile_no":2,"pokemon_id":26}]}}
                """)
            .On("/tracking?include_descriptions=true", """
                {"human":{"current_profile_no":1},"profiles":[{"uid":2,"profile_no":2}],
                 "tracking":{"pokemon":[{"uid":5,"pokemon_id":25}],"incident":[{"uid":9,"display_type":8}]}}
                """);

        var all = await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        Assert.Equal(2, handler.Requests.Count);
        var pokemon = all.GetProperty("pokemon").EnumerateArray().ToList();
        Assert.Equal([(6, 2), (5, 1)], pokemon.Select(r => (r.GetProperty("uid").GetInt32(), r.GetProperty("profile_no").GetInt32())));
        Assert.Equal(1, all.GetProperty("invasion")[0].GetProperty("profile_no").GetInt32());
        Assert.Equal(1, all.GetProperty("profile").GetArrayLength());
    }

    [Fact]
    public async Task ActiveProfileRulesTheSnapshotAlreadyCarriesAreNotAppendedTwice()
    {
        // jfberry/PoracleNG#331: all_profiles also returns the rules of an active profile with no profiles row,
        // profile_no and all. The second read still runs for older servers and must not add them again.
        var handler = new Router()
            .On("all_profiles=true", """
                {"human":{"current_profile_no":1},
                 "profiles":[{"uid":2,"id":"user1","profile_no":2,"name":"b","area":[],"latitude":0,"longitude":0,"active_hours":[]}],
                 "tracking":{"pokemon":[{"uid":5,"profile_no":1,"pokemon_id":25},{"uid":6,"profile_no":2,"pokemon_id":26}],
                             "invasion":[{"uid":9,"profile_no":1,"grunt_type":"water"}],
                             "incident":[{"uid":9,"profile_no":1,"display_type":8}]}}
                """)
            .On("/tracking?include_descriptions=true", """
                {"human":{"current_profile_no":1},
                 "tracking":{"pokemon":[{"uid":5,"pokemon_id":25}],
                             "invasion":[{"uid":9,"grunt_type":"water"}],
                             "incident":[{"uid":9,"display_type":8}]}}
                """);

        var all = await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([5, 6], all.GetProperty("pokemon").EnumerateArray().Select(r => r.GetProperty("uid").GetInt32()));
        Assert.Equal(
            ["water", "kecleon"],
            all.GetProperty("invasion").EnumerateArray().Select(r => r.GetProperty("grunt_type").GetString()));
    }

    [Fact]
    public async Task AnAppendedRuleSharingAUidWithAnotherTypeIsKept()
    {
        // uids are per table. Pokemon 5 and invasion 9 on profile 0 say nothing about incident 5 or incident 9
        // on the active profile, even though incident rules land in the same invasion list.
        var handler = new Router()
            .On("all_profiles=true", """
                {"human":{"current_profile_no":1},
                 "profiles":[{"uid":1,"id":"user1","profile_no":0,"name":"a","area":[],"latitude":0,"longitude":0,"active_hours":[]}],
                 "tracking":{"pokemon":[{"uid":5,"profile_no":0,"pokemon_id":25}],
                             "invasion":[{"uid":9,"profile_no":0,"grunt_type":"water"}]}}
                """)
            .On("/tracking?include_descriptions=true", """
                {"human":{"current_profile_no":1},
                 "tracking":{"incident":[{"uid":5,"display_type":8},{"uid":9,"display_type":7}]}}
                """);

        var all = await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        var invasion = all.GetProperty("invasion").EnumerateArray()
            .Select(r => (r.GetProperty("uid").GetInt32(), r.GetProperty("grunt_type").GetString(), r.GetProperty("profile_no").GetInt32()))
            .ToList();
        Assert.Contains((5, "kecleon", 1), invasion);
        Assert.Contains((9, "water", 0), invasion);
        Assert.Equal(3, invasion.Count);
        Assert.Single(all.GetProperty("pokemon").EnumerateArray());
    }

    [Fact]
    public async Task AnUnknownTypeThrowsBeforeAnyRequest()
    {
        var handler = new Router();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateSut(handler).GetByUserAsync("unknown_type", "user1"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AHumanWithProfilesDoesNotNeedTheFallback()
    {
        var handler = new Router().On("/tracking", AllProfiles);

        await CreateSut(handler).GetAllTrackingAllProfilesAsync("user1");

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TheAllProfilesReadForAMissingHumanIsAGoneAccount() =>
        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(new Router().On("/tracking", HumanNotFound, HttpStatusCode.NotFound, "application/problem+json"))
                .GetAllTrackingAllProfilesAsync("user1"));

    [Fact]
    public async Task TheAllProfilesReadOnAServerWithoutTheRouteIsUnsupported()
    {
        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(new Router().On("/tracking", "404 page not found", HttpStatusCode.NotFound, "text/plain"))
                .GetAllTrackingAllProfilesAsync("user1"));

        Assert.Equal("All alarms", ex.Feature);
    }

    // ── harness ──────────────────────────────────────────────────

    private static PoracleTrackingProxy CreateSut(Router handler, ILogger<PoracleTrackingProxy>? logger = null)
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
            PoracleTrackingProxyTests.AnyAreaPolicy(),
            new MemoryCache(new MemoryCacheOptions()),
            logger ?? Mock.Of<ILogger<PoracleTrackingProxy>>());
    }

    private sealed record Sent(HttpMethod Method, string Url);

    /// <summary>Answers by the first registered fragment the URL contains (later registrations are tried first).</summary>
    private sealed class Router : HttpMessageHandler
    {
        private readonly List<(string Fragment, string Body, HttpStatusCode Status, string ContentType)> _routes = [];

        public List<Sent> Requests { get; } = [];

        public Router On(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
        {
            this._routes.Insert(0, (fragment, body, status, contentType));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            this.Requests.Add(new Sent(request.Method, url));

            foreach (var (fragment, body, status, contentType) in this._routes)
            {
                if (url.Contains(fragment, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body, Encoding.UTF8, contentType),
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("404 page not found", Encoding.UTF8, "text/plain"),
            });
        }
    }

    private sealed class CapturingLogger : ILogger<PoracleTrackingProxy>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                this.Warnings.Add(formatter(state, exception));
            }
        }
    }
}
