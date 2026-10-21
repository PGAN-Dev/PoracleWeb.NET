using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The areas and location a new profile is given after create, duplicate and import, driven through each
/// controller with a real <see cref="PoracleHumanProxy"/> so what is asserted is what reaches PoracleNG.
/// See #993.
/// </summary>
/// <remarks>
/// <para>
/// The geography goes to the new profile through PoracleNG's v2 <c>?profile=</c> writes, and a refusal
/// reaches the caller: there is no direct <c>profiles</c> write behind it any more.
/// </para>
/// <para>
/// The bodies checked are the ones each controller builds, against the schemas in the openapi fixture.
/// </para>
/// </remarks>
public class NewProfileGeographyTests : ControllerTestBase
{
    private const string ApiAddress = "http://localhost:3030";
    private const string UserId = "123456789";
    private const int NewProfileNo = 4;

    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IProfileOverviewService> _overview = new();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IUserRoleResolver> _roleResolver = new();
    private readonly Mock<IUserGeofenceRepository> _userGeofences = new();

    private static readonly Profile Source = new()
    {
        Id = UserId, ProfileNo = 1, Name = "Work",
        Area = """["downtown","my fence"]""", Latitude = 37.5, Longitude = -77.4,
    };

    public NewProfileGeographyTests()
    {
        this._jwtService
            .Setup(j => j.GenerateTokenWithReplacedProfile(
                It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()))
            .Returns("jwt");
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync(UserId, 1)).ReturnsAsync(Source);
        this._profileService.Setup(s => s.GetByUserAsync(UserId)).ReturnsAsync([Source]);
        this._userGeofences.Setup(r => r.GetAllAsync()).ReturnsAsync([]);
        this._overview.Setup(s => s.DuplicateProfileAsync(UserId, 1, NewProfileNo)).ReturnsAsync(2);
        this._overview.Setup(s => s.ImportAlarmsAsync(UserId, NewProfileNo, It.IsAny<JsonElement>())).ReturnsAsync(1);
    }

    /// <summary>What each endpoint should write, and the request that makes it do so.</summary>
    public static TheoryData<string> Endpoints() => ["create", "duplicate", "overview-duplicate", "import"];

    private static (string[] Areas, double Latitude, double Longitude) Expected(string endpoint) => endpoint switch
    {
        "create" => (["downtown", "my fence"], 41.65, -83.53),
        "duplicate" or "overview-duplicate" => (["downtown", "my fence"], 37.5, -77.4),
        _ => ([], 0, 0),
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WhereTheServerDeclaresItTheGeographyGoesToTheNewProfileThroughV2(string endpoint)
    {
        var handler = new Routed();
        await this.RunAsync(endpoint, handler, declared: true);

        var expected = Expected(endpoint);

        var areas = Assert.Single(handler.Requests, r => r.Path == "/api/v2/humans/123456789/areas");
        Assert.Equal($"profile={NewProfileNo}", areas.Query);
        using var areasBody = JsonDocument.Parse(areas.Body!);
        AssertDeclares("V2SetAreasBody", areasBody.RootElement);
        Assert.Equal(expected.Areas, areasBody.RootElement.GetProperty("areas").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(expected.Areas.Length > 0, areasBody.RootElement.TryGetProperty("trusted", out _));

        var location = Assert.Single(handler.Requests, r => r.Path == "/api/v2/humans/123456789/location");
        Assert.Equal($"profile={NewProfileNo}", location.Query);
        using var locationBody = JsonDocument.Parse(location.Body!);
        AssertDeclares("V2LocationBody", locationBody.RootElement);
        Assert.Equal(expected.Latitude, locationBody.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(expected.Longitude, locationBody.RootElement.GetProperty("longitude").GetDouble());

    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AV2RefusalReachesTheCallerAndNothingIsWrittenDirectly(string endpoint)
    {
        var handler = new Routed(refuseGeography: true);

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this.RunAsync(endpoint, handler, declared: true));

        Assert.Contains(handler.Requests, r => r.Path == "/api/v2/humans/123456789/areas");
    }

    private async Task RunAsync(string endpoint, Routed handler, bool declared)
    {
        var proxy = CreateProxy(handler, declared);

        IActionResult result;
        if (endpoint is "create" or "duplicate")
        {
            var controller = new ProfileController(
                this._profileService.Object, Mock.Of<IHumanService>(), proxy,
                this._jwtService.Object, this._roleResolver.Object, this._userGeofences.Object,
                NullLogger<ProfileController>.Instance);
            SetupUser(controller, UserId);

            result = endpoint == "create"
                ? await controller.Create(new Profile
                {
                    Name = "probe-two",
                    Area = """["downtown","my fence"]""",
                    Latitude = 41.65,
                    Longitude = -83.53,
                    ActiveHours = """[{"day":1,"hours":8,"mins":0}]""",
                })
                : await controller.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Work copy" });

            Assert.IsType<CreatedAtActionResult>(result);
            return;
        }

        var overview = new ProfileOverviewController(
            this._overview.Object, this._profileService.Object, proxy,
            this._jwtService.Object, this._roleResolver.Object, NullLogger<ProfileOverviewController>.Instance);
        SetupUser(overview, UserId);

        result = endpoint == "overview-duplicate"
            ? await overview.DuplicateProfile(1, new ProfileOverviewDuplicateRequest("Work copy"))
            : await overview.ImportProfile(new ProfileOverviewImportRequest(
                "Imported", 1, JsonDocument.Parse("""{"pokemon":[{"pokemon_id":1}]}""").RootElement.Clone()));

        Assert.IsType<OkObjectResult>(result);
    }

    private static PoracleHumanProxy CreateProxy(Routed handler, bool declared) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build(),
        PoracleHumanProxyTests.V2Schema(new PoracleV2Capabilities
        {
            Read = true,
            TrustedSetAreas = true,
        }),
        PoracleHumanProxyTests.AreaSecurityPolicy(confirmedDisabled: true),
        Mock.Of<ILogger<PoracleHumanProxy>>());

    /// <summary>Every property of the body is one the named schema in the openapi fixture declares.</summary>
    private static void AssertDeclares(string schema, JsonElement body)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixture()));
        var declared = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var property in body.EnumerateObject())
        {
            Assert.Contains(property.Name, declared);
        }
    }

    private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string? here = null) =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "poracleng-openapi-next.json");

    private sealed record Sent(HttpMethod Method, string Path, string Query, string? Body);

    /// <summary>
    /// Answers by route, the way PoracleNG would for these calls. The profile create reports the number
    /// assigned; the two geography writes answer what a live 5.3.0 answered, or 422 when told to refuse.
    /// </summary>
    private sealed class Routed(bool refuseGeography = false) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = request.RequestUri!;
            this.Requests.Add(new Sent(request.Method, uri.AbsolutePath, uri.Query.TrimStart('?'), body));

            var geography = uri.Query.Contains("profile=", StringComparison.Ordinal);
            var (status, reply, type) = uri.AbsolutePath switch
            {
                _ when geography && refuseGeography =>
                    (HttpStatusCode.UnprocessableEntity,
                     """{"title":"Unprocessable Entity","status":422,"detail":"validation failed"}""",
                     "application/problem+json"),
                "/api/v2/humans/123456789/profiles" when request.Method == HttpMethod.Post =>
                    (HttpStatusCode.OK, $$"""{"profile_no":{{NewProfileNo}}}""", "application/json"),
                "/api/v2/humans/123456789/areas" =>
                    (HttpStatusCode.OK, AreasAnswer(body), "application/json"),
                _ => (HttpStatusCode.OK, """{"status":"ok"}""", "application/json"),
            };

            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, type) };
        }

        private static string AreasAnswer(string? body)
        {
            var areas = JsonDocument.Parse(body!).RootElement.GetProperty("areas").GetRawText();
            return $$"""{"areas":{{areas}},"rejected":[]}""";
        }
    }
}
