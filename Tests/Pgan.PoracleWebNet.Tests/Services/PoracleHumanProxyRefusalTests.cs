using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// What the human, profile, area and location proxy does when PoracleNG refuses.
/// </summary>
/// <remarks>
/// <para>
/// Every status and body quoted here was taken from a live PoracleNG: 5.1.0 on :3040 and 5.2.1 on :3042,
/// probed with deliberately invalid requests. The v1 routes this proxy calls answer 400 identically on
/// both; the 422 bodies come from 5.2.1's <c>/api/v2/humans</c> surface, which the same mistakes reach
/// once a call site moves over.
/// </para>
/// <para>
/// Half of these assert what must keep working rather than what must now fail. A refusal filter that also
/// swallows the account-gone 404 costs the SPA its sign-out; one that swallows the delete-place 409 costs
/// the user the list of alarms blocking the delete. Both are cheaper to catch here than in production.
/// </para>
/// </remarks>
public class PoracleHumanProxyRefusalTests
{
    private static PoracleHumanProxy CreateSut(MockHandler handler) =>
        new(new HttpClient(handler), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = "http://localhost:3030",
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build());

    private static PoracleHumanProxy Refusing(HttpStatusCode status, string body) =>
        CreateSut(new MockHandler(status, body));

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    // ──────────────────────────────────────────────────────────────
    // 400 on the v1 routes -- the caller's mistake, reported as one
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminDisabledPassesPoracleWordingThroughOn400()
    {
        // Live: POST /api/humans/{id}/adminDisabled {"nope":1} on 5.1.0 and 5.2.1.
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"state is required (true/false)","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.AdminDisabledAsync("user1", true));

        Assert.Equal("state is required (true/false)", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SetLocationPassesPoracleWordingThroughOn400()
    {
        // Live: POST /api/humans/{id}/setLocation/abc/def.
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid latitude","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SetLocationAsync("user1", 0, 0));

        Assert.Equal("invalid latitude", ex.Message);
    }

    [Fact]
    public async Task SetAreasPassesPoracleWordingThroughOn400()
    {
        // Live: POST /api/humans/{id}/setAreas with an object instead of an array.
        var sut = Refusing(
            HttpStatusCode.BadRequest,
            """{"message":"json: cannot unmarshal object into Go value of type []string","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SetAreasAsync("user1", ["area1"]));

        Assert.Contains("cannot unmarshal object", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateHumanPassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"id and name are required","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.CreateHumanAsync(Body("""{"garbage":true}""")));

        Assert.Equal("id and name are required", ex.Message);
    }

    [Fact]
    public async Task UpdateProfilePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"profile_no must be specified","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.UpdateProfileAsync("user1", Body("""{"bad":1}""")));

        Assert.Equal("profile_no must be specified", ex.Message);
    }

    [Fact]
    public async Task AddProfilePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid request body","status":"error"}""");

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.AddProfileAsync("user1", Body("""{"bad":1}""")));
    }

    [Fact]
    public async Task DeleteProfilePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid profile_no","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.DeleteProfileAsync("user1", 1));

        Assert.Equal("invalid profile_no", ex.Message);
    }

    [Fact]
    public async Task CopyProfilePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid from profile number","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.CopyProfileAsync("user1", 1, 2));

        Assert.Equal("invalid from profile number", ex.Message);
    }

    [Fact]
    public async Task SwitchProfilePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid profile number","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SwitchProfileAsync("user1", 1));

        Assert.Equal("invalid profile number", ex.Message);
    }

    [Fact]
    public async Task AddPlacePassesPoracleWordingThroughOn400()
    {
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid request body","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.AddPlaceAsync("user1", new SavedPlace { Label = "home", Latitude = 1, Longitude = 2 }));

        Assert.Equal("invalid request body", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────
    // 422 -- the same refusal wearing v2's number
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadsProblemJsonFieldErrorsOn422()
    {
        // Live: POST /api/v2/humans/{id}/admin-disable {"nope":1} on 5.2.1.
        var sut = Refusing(HttpStatusCode.UnprocessableEntity, """
            {"title":"Unprocessable Entity","status":422,"detail":"validation failed",
             "errors":[{"message":"expected required property disabled to be present","location":"body"},
                       {"message":"unexpected property","location":"body.nope"}]}
            """);

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.AdminDisabledAsync("user1", true));

        Assert.Contains("expected required property disabled to be present", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nope: unexpected property", ex.Message, StringComparison.Ordinal);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ReadsProblemJsonDetailWhenThereAreNoFieldErrors()
    {
        var sut = Refusing(
            HttpStatusCode.UnprocessableEntity,
            """{"title":"Unprocessable Entity","status":422,"detail":"latitude must be between -90 and 90"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SetLocationAsync("user1", 999, 0));

        Assert.Equal("latitude must be between -90 and 90", ex.Message);
    }

    [Fact]
    public async Task SaysSomethingOtherThanAlarmWhenTheRefusalExplainsNothing()
    {
        // PoracleProblemDetails' own fallback names an alarm, which is wrong for a profile or a place.
        var sut = Refusing(HttpStatusCode.BadRequest, "{}");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.UpdateProfileAsync("user1", Body("{}")));

        Assert.DoesNotContain("alarm", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ex.Message);
    }

    // ──────────────────────────────────────────────────────────────
    // 404 -- account gone, and the two 404s that are not
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMissingAccountIsStillAccountGone()
    {
        // The SPA signs out only on 401, and AccountGoneExceptionFilter is what produces it. Live wording
        // from both 5.1.0 and 5.2.1.
        var sut = Refusing(HttpStatusCode.NotFound, """{"message":"User not found","status":"error"}""");

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.StartAsync("deleted-user"));
    }

    [Theory]
    [InlineData("/stop")]
    [InlineData("/setAreas")]
    [InlineData("/profiles")]
    public async Task EveryWritePathStillReportsAMissingAccountAsGone(string path)
    {
        var sut = Refusing(HttpStatusCode.NotFound, """{"message":"User not found","status":"error"}""");

        Task Call() => path switch
        {
            "/stop" => sut.StopAsync("deleted-user"),
            "/setAreas" => sut.SetAreasAsync("deleted-user", ["area1"]),
            _ => sut.GetProfilesAsync("deleted-user"),
        };

        await Assert.ThrowsAsync<AccountGoneException>(Call);
    }

    [Fact]
    public async Task TheV2WordingForAMissingAccountCountsToo()
    {
        var sut = Refusing(HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"human not found"}""");

        await Assert.ThrowsAsync<AccountGoneException>(() => sut.StopAsync("deleted-user"));
    }

    [Fact]
    public async Task AMissingProfileIsNotAMissingAccount()
    {
        // Live: POST /api/humans/{id}/switchProfile/99 answers 404 "Profile not found" on both servers.
        // Calling that account-gone signed the user out of a working session -- and inside
        // ProfileOverviewService's restore-the-profile finally, it did so while hiding the real failure.
        var sut = Refusing(HttpStatusCode.NotFound, """{"message":"Profile not found","status":"error"}""");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.SwitchProfileAsync("user1", 99));

        Assert.Equal("Profile not found", ex.Message);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ARouteThisBuildDoesNotHaveIsNotAMissingAccount()
    {
        // gin answers a plaintext body for an absent route. Signing the user out over it told them their
        // account was deleted when the truth was an older PoracleNG.
        var sut = Refusing(HttpStatusCode.NotFound, "404 page not found");

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => sut.CopyProfileAsync("user1", 1, 2));

        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("404 page not found", ex.Message, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────
    // What must keep working
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task APlaceStillInUseIsStillAConflictNamingTheAlarms()
    {
        var handler = new MockHandler(
            HttpStatusCode.Conflict,
            """{"message":"location in use","referencing_rules":["monster 25","raid 5"]}""");
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<PlaceInUseException>(() => sut.DeletePlaceAsync("user1", "home"));

        Assert.Equal(["monster 25", "raid 5"], ex.ReferencingRules);
    }

    [Fact]
    public async Task AServerFaultIsStillAServerFault()
    {
        // 500, 502 and the rest are not the caller's problem and must not be dressed up as a 400.
        var sut = Refusing(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetAreasAsync("user1", ["area1"]));
    }

    [Fact]
    public async Task AConflictOutsideDeletePlaceIsStillAServerFault()
    {
        // Only the delete-place route gives 409 a meaning. Nothing else should start reading one.
        var sut = Refusing(HttpStatusCode.Conflict, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.CreateHumanAsync(Body("{}")));
    }

    [Fact]
    public async Task A200StillSucceedsOnEveryPathThisTouches()
    {
        var handler = new MockHandler(HttpStatusCode.OK, """{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.CreateHumanAsync(Body("""{"id":"u1","name":"n"}"""));
        await sut.StartAsync("user1");
        await sut.StopAsync("user1");
        await sut.AdminDisabledAsync("user1", true);
        await sut.SetLocationAsync("user1", 1, 2);
        await sut.SetAreasAsync("user1", ["area1"]);
        await sut.SwitchProfileAsync("user1", 1);
        await sut.AddProfileAsync("user1", Body("""{"name":"p"}"""));
        await sut.UpdateProfileAsync("user1", Body("""{"profile_no":1}"""));
        await sut.DeleteProfileAsync("user1", 1);
        await sut.CopyProfileAsync("user1", 1, 2);
        await sut.DeletePlaceAsync("user1", "home");

        Assert.Null(await sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" }));
    }

    [Fact]
    public async Task AddPlaceStillReportsTheRefusalHiddenInsideA200()
    {
        // PoracleNG answers per row so a batch can partly succeed. This is not a status-code refusal and
        // must not turn into one.
        var handler = new MockHandler(HttpStatusCode.OK, """{"results":[{"error":"label already used"}]}""");
        var sut = CreateSut(handler);

        Assert.Equal("label already used", await sut.AddPlaceAsync("user1", new SavedPlace { Label = "home" }));
    }

    [Fact]
    public async Task ReadsThatAnswerNullOnFailureStillAnswerNull()
    {
        // GetHumanAsync and CheckLocationAsync are read paths whose callers branch on null. Turning their
        // failures into throws would break HumanService, ProfileService and TestAlertService at once.
        var sut = Refusing(HttpStatusCode.BadRequest, """{"message":"invalid latitude","status":"error"}""");

        Assert.Null(await sut.GetHumanAsync("user1"));
        Assert.Null(await sut.CheckLocationAsync("user1", 999, 999));
        Assert.Null(await sut.GetAreasAsync("user1"));
    }

    private sealed class MockHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
    }
}
