using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The incident proxy's wire behaviour on PoracleNG 5.4.0: trusted override areas, the dry run, uids in the
/// bulk-delete body, and how a server too old for the route is told apart from a refusal.
/// </summary>
public class PoracleIncidentProxyTests
{
    private const string Api = "http://localhost:3030";
    private const string Route = $"{Api}/api/v2/humans/u1/tracking/incident";

    private static PoracleIncidentProxy CreateSut(Handler handler, IUserAreaPolicy? policy = null) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = Api,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build(),
        policy ?? Mock.Of<IUserAreaPolicy>(),
        NullLogger<PoracleIncidentProxy>.Instance);

    private const string CreatedOk = """{"created":[{"uid":740,"display_type":9}],"updated":null,"unchanged":null}""";

    // ── bulk delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IncidentBulkDeleteSendsUidsInBody()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, """{"deleted":[{"uid":1},{"uid":2}]}"""));

        var deleted = await CreateSut(handler).BulkDeleteByUidsAsync("u1", [1, 2, 2]);

        Assert.Equal(2, deleted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"{Route}?silent=true", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal([1, 2], body.RootElement.GetProperty("uids").EnumerateArray().Select(u => u.GetInt32()));
    }

    [Fact]
    public async Task IncidentBulkDeleteOnAServerTooOldIsUnsupported()
    {
        // Before 5.4.0 the bulk route required ?uid and had no body form, so it names the missing parameter.
        var handler = new Handler(new Reply(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"errors":[{"location":"query.uid","message":"required"}]}""",
            "application/problem+json"));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(() => CreateSut(handler).BulkDeleteByUidsAsync("u1", [1]));

        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task IncidentBulkDeleteOtherRefusalsAreNotCalledTooOld()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"uids must not be empty"}""",
            "application/problem+json"));

        await Assert.ThrowsAsync<AlarmValidationException>(() => CreateSut(handler).BulkDeleteByUidsAsync("u1", [1]));
    }

    // ── patch ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncidentPatchSendsUidsAndOnlyTheNamedSettings()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.OK,
            """{"dry_run":false,"updated":[{"uid":1,"display_type":9}],"unchanged":[{"uid":2,"display_type":8}],"refused":null}"""));

        var result = await CreateSut(handler).PatchAsync("u1", [1, 2], new TrackingPatchSet(Distance: 750));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"{Route}?silent=true&trusted=true", request.Url);
        Assert.Equal("""{"uids":[1,2],"set":{"distance":750}}""", request.Body);
        Assert.Equal([1], result.Updated);
        Assert.Equal([2], result.Unchanged);
        Assert.Empty(result.Refused);
    }

    [Fact]
    public async Task IncidentPatchOfARuleLimitedToTheUsersOwnFenceIsSentTrusted()
    {
        // PoracleNG re-validates the whole stored rule on a PATCH, and untrusted it refuses a user's own drawn
        // fence as invalid. A PATCH cannot name an area, so there is nothing for the area policy to authorise.
        var handler = new Handler(new Reply(
            HttpStatusCode.OK, """{"dry_run":false,"updated":[{"uid":7,"display_type":9}],"unchanged":null,"refused":null}"""));
        var policy = new Mock<IUserAreaPolicy>(MockBehavior.Strict);

        var result = await CreateSut(handler, policy.Object).PatchAsync("u1", [7], new TrackingPatchSet(Clean: true));

        Assert.Contains("trusted=true", Assert.Single(handler.Requests).Url, StringComparison.Ordinal);
        Assert.Equal([7], result.Updated);
    }

    [Fact]
    public async Task IncidentPatchMapsRefusals()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.OK,
            """{"updated":null,"unchanged":null,"refused":[{"uid":3,"reason":"conflict","detail":"duplicate","conflicts_with":4}]}"""));

        var result = await CreateSut(handler).PatchAsync("u1", [3], new TrackingPatchSet(Distance: 750));

        Assert.Equal(new PatchRefusal(3, PatchRefusalReasons.Conflict, "duplicate", 4), Assert.Single(result.Refused));
    }

    [Fact]
    public async Task IncidentPatchOnAServerWithoutTheRouteIsUnsupported()
    {
        var handler = new Handler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(handler).PatchAsync("u1", [3], new TrackingPatchSet(Distance: 750)));

        Assert.Equal("saving alarms", ex.Feature);
        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task IncidentPatchWithNoUidsSendsNothing()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, "{}"));

        await CreateSut(handler).PatchAsync("u1", [], new TrackingPatchSet(Distance: 750));

        Assert.Empty(handler.Requests);
    }

    // ── trusted override areas ───────────────────────────────────────────────

    [Fact]
    public async Task PokestopEventOwnFenceSendsTrusted()
    {
        var policy = new Mock<IUserAreaPolicy>();
        var handler = new Handler(new Reply(HttpStatusCode.OK, CreatedOk));

        await CreateSut(handler, policy.Object).CreateAsync("u1", [
            new PokestopEvent { DisplayType = PokestopEventTypes.Showcase, OverrideAreas = ["my fence"] },
        ]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{Route}?silent=true&trusted=true", request.Url);
        policy.Verify(
            p => p.EnsureOverrideAreasAllowedAsync(
                "u1",
                It.Is<IReadOnlyCollection<string>>(n => n.Count == 1 && n.Contains("my fence")),
                null),
            Times.Once);
    }

    [Fact]
    public async Task ACreateWithoutOverrideAreasIsNotTrustedAndAsksNoPolicy()
    {
        var policy = new Mock<IUserAreaPolicy>(MockBehavior.Strict);
        var handler = new Handler(new Reply(HttpStatusCode.OK, CreatedOk));

        await CreateSut(handler, policy.Object).CreateAsync("u1", [new PokestopEvent { DisplayType = PokestopEventTypes.Showcase, Distance = 500 }]);

        Assert.Equal($"{Route}?silent=true", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task ACreateTheAreaPolicyRefusesSendsNothing()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, CreatedOk));
        var sut = CreateSut(handler, RealPolicy(selectable: ["downtown"]));

        var ex = await Assert.ThrowsAsync<AlarmValidationException>(() => sut.CreateAsync("u1", [
            new PokestopEvent { DisplayType = PokestopEventTypes.Showcase, OverrideAreas = ["someone else's park"] },
        ]));

        Assert.Contains("someone else's park", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AReplaceKeepingAStoredLegacyAreaReadsTheRuleAndSendsTrusted()
    {
        // "old town" is no longer selectable, but the rule already holds it. An edit that keeps it must save.
        var handler = new Handler(
            new Reply(HttpStatusCode.OK, """{"rules":[{"uid":740,"display_type":9,"override_areas":["old town"]}]}"""),
            new Reply(HttpStatusCode.OK, """{"created":null,"updated":[{"uid":740,"display_type":9}],"unchanged":null}"""));
        var sut = CreateSut(handler, RealPolicy(selectable: ["downtown"]));

        var result = await sut.ReplaceAsync("u1", 740, new PokestopEvent { DisplayType = 9, OverrideAreas = ["old town"] });

        Assert.Equal(740, result.PrimaryUid);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal($"{Route}/740", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal($"{Route}/740?silent=true&trusted=true", handler.Requests[1].Url);
    }

    [Fact]
    public async Task AReplaceWithASelectableAreaReadsNoStoredRule()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, """{"updated":[{"uid":740,"display_type":9}]}"""));
        var sut = CreateSut(handler, RealPolicy(selectable: ["downtown"]));

        await sut.ReplaceAsync("u1", 740, new PokestopEvent { DisplayType = 9, OverrideAreas = ["downtown"] });

        Assert.Equal($"{Route}/740?silent=true&trusted=true", Assert.Single(handler.Requests).Url);
    }

    // ── dry run and the write envelope ───────────────────────────────────────

    [Fact]
    public async Task ADryRunCarriesTheFlag()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, """{"dry_run":true,"created":[{"uid":0,"display_type":9}]}"""));

        var result = await CreateSut(handler).CreateAsync("u1", [new PokestopEvent { DisplayType = 9 }], dryRun: true);

        Assert.Equal($"{Route}?silent=true&dry_run=true", Assert.Single(handler.Requests).Url);
        Assert.Single(result.Created);
    }

    [Fact]
    public async Task ACreateOnATargetProfileNamesIt()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, CreatedOk));

        await CreateSut(handler).CreateAsync("u1", [new PokestopEvent { DisplayType = 9 }], 5, dryRun: true);

        Assert.Equal($"{Route}?silent=true&profile=5&dry_run=true", Assert.Single(handler.Requests).Url);
    }

    [Fact]
    public async Task AReplaceOnATargetProfileNamesItOnTheStoredReadAndThePut()
    {
        var handler = new Handler(
            new Reply(HttpStatusCode.OK, """{"rules":[{"uid":740,"display_type":9,"override_areas":["old town"]}]}"""),
            new Reply(HttpStatusCode.OK, """{"updated":[{"uid":740,"display_type":9}]}"""));
        var sut = CreateSut(handler, RealPolicy(selectable: ["downtown"]));

        await sut.ReplaceAsync("u1", 740, new PokestopEvent { DisplayType = 9, OverrideAreas = ["old town"] }, 5);

        Assert.Equal($"{Route}/740?profile=5", handler.Requests[0].Url);
        Assert.Equal($"{Route}/740?silent=true&profile=5&trusted=true", handler.Requests[1].Url);
    }

    [Fact]
    public async Task AnUpdatedRuleCarriesWhatItHeldBefore()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.OK,
            """{"updated":[{"uid":732,"display_type":9,"distance":900,"previous":{"uid":732,"display_type":9,"distance":500,"template":"x"}}]}"""));

        var result = await CreateSut(handler).CreateAsync("u1", [new PokestopEvent { DisplayType = 9, Distance = 900 }]);

        var previous = Assert.Single(result.Previous!)!;
        Assert.Equal(732, previous.Uid);
        Assert.Equal(500, previous.Distance);
        Assert.Equal("x", previous.Template);
    }

    [Fact]
    public async Task APingIsSentAndReadBack()
    {
        var handler = new Handler(new Reply(HttpStatusCode.OK, """{"updated":[{"uid":5,"display_type":8,"ping":"<@&1>"}]}"""));

        var result = await CreateSut(handler).ReplaceAsync("u1", 5, new PokestopEvent { DisplayType = 8, Ping = "<@&1>" });

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        Assert.Equal("<@&1>", body.RootElement.GetProperty("ping").GetString());
        Assert.Equal("<@&1>", Assert.Single(result.Updated).Ping);
    }

    // ── status mapping ───────────────────────────────────────────────────────

    [Fact]
    public async Task AWriteToAMissingRouteIsATooOldServer()
    {
        var handler = new Handler(new Reply(HttpStatusCode.NotFound, "404 page not found", "text/plain"));

        var ex = await Assert.ThrowsAsync<PoracleUnsupportedException>(
            () => CreateSut(handler).CreateAsync("u1", [new PokestopEvent { DisplayType = 9 }]));

        Assert.Equal("saving alarms", ex.Feature);
        Assert.Equal("PoracleNG 5.4.0", ex.Requires);
    }

    [Fact]
    public async Task AReplaceConflictIsTheTakeoverMessage()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.Conflict, """{"title":"Conflict","status":409,"detail":"duplicate rule"}""", "application/problem+json"));

        var ex = await Assert.ThrowsAsync<TrackingConflictException>(
            () => CreateSut(handler).ReplaceAsync("u1", 5, new PokestopEvent { DisplayType = 9 }));

        Assert.Equal("Another alarm of this type already uses those settings. Edit or remove that one instead.", ex.Message);
    }

    [Fact]
    public async Task AReplaceOfARuleThatIsGoneIsRuleNotFoundNotASignOut()
    {
        // An event edit racing a delete, or a scheduler profile switch, finds no rule. That is the rule gone,
        // not the account: mapping it to AccountGoneException answered 401 and signed the user out.
        var handler = new Handler(new Reply(
            HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"incident rule 5 not found for this human"}""",
            "application/problem+json"));

        var ex = await Assert.ThrowsAsync<TrackingRuleNotFoundException>(
            () => CreateSut(handler).ReplaceAsync("u1", 5, new PokestopEvent { DisplayType = 9 }));

        Assert.Equal("incident", ex.TrackingType);
        Assert.Contains("not found for this human", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplaceForAHumanPoracleNgDoesNotHoldIsAccountGone()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""", "application/problem+json"));

        await Assert.ThrowsAsync<AccountGoneException>(
            () => CreateSut(handler).ReplaceAsync("u1", 5, new PokestopEvent { DisplayType = 9 }));
    }

    [Fact]
    public async Task AReadForAHumanPoracleNgDoesNotHoldIsAccountGone()
    {
        var handler = new Handler(new Reply(
            HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""", "application/problem+json"));

        await Assert.ThrowsAsync<AccountGoneException>(() => CreateSut(handler).GetByUserAsync("u1"));
    }

    private static UserAreaPolicy RealPolicy(string[] selectable)
    {
        var human = new Mock<IPoracleHumanProxy>();
        human.Setup(h => h.GetAreaListAsync("u1")).ReturnsAsync([]);
        human.Setup(h => h.GetAvailableAreasAsync("u1"))
            .ReturnsAsync(selectable.Select(n => new V2AreaInfo(n, true, false)).ToList());
        var fences = new Mock<IUserGeofenceRepository>();
        fences.Setup(f => f.GetByHumanIdAsync("u1")).ReturnsAsync([]);
        return new UserAreaPolicy(human.Object, fences.Object);
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json");

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    /// <summary>Answers each request with the next reply, in order.</summary>
    private sealed class Handler(params Reply[] replies) : HttpMessageHandler
    {
        private readonly Queue<Reply> _replies = new(replies);

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(new Sent(request.Method, request.RequestUri!.ToString(), body));
            var reply = this._replies.Count > 1 ? this._replies.Dequeue() : this._replies.Peek();
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, reply.ContentType),
            };
        }
    }
}
