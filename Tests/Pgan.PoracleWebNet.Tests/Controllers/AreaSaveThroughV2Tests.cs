using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The Areas page save end to end through a real <see cref="PoracleHumanProxy"/>: what
/// PoracleNG is sent, that a user-drawn fence it refuses is still handed to
/// <c>PreserveOwnedAreasInHumanAsync</c>, and that the response is the stored list read back. See #995.
/// </summary>
/// <remarks>
/// The legitimate case this guards: a user saving the Areas page with one of their own fences ticked.
/// PoracleNG refuses that fence on both surfaces because PoracleWeb.NET serves it
/// <c>userSelectable:false</c> -- v1 by dropping it silently, v2 by listing it under <c>rejected</c>
/// beside a 200, both verified live -- and the save has to succeed either way.
/// </remarks>
public class AreaSaveThroughV2Tests : ControllerTestBase
{
    private const string ApiAddress = "http://localhost:3030";
    private const string UserId = "123456789";

    [Fact]
    public async Task AUserFencePoracleRefusesIsStillPreservedAndTheSaveSucceeds()
    {
        var handler = new Scripted(
            new Reply("""{"areas":["downtown"],"rejected":["my park"]}"""),
            // The read-back after PreserveOwnedAreasInHumanAsync put the fence back.
            new Reply("""{"human":{"id":"123456789","area":["downtown","my park"],"current_profile_no":1}}"""));

        var geofences = new Mock<IUserGeofenceService>();
        geofences
            .Setup(s => s.PreserveOwnedAreasInHumanAsync(UserId, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["my park"]);

        var sut = new AreaController(
            CreateProxy(handler),
            Mock.Of<IPoracleApiProxy>(),
            geofences.Object,
            Mock.Of<ILogger<AreaController>>());
        SetupUser(sut, UserId);

        var result = await sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["Downtown", "My Park"] });

        var save = handler.Requests[0];
        Assert.Equal($"{ApiAddress}/api/v2/humans/{UserId}/areas", save.Url);

        using var body = JsonDocument.Parse(save.Body!);
        var sentAreas = body.RootElement.GetProperty("areas");
        Assert.Equal(["downtown", "my park"], sentAreas.EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.False(body.RootElement.TryGetProperty("trusted", out _));

        geofences.Verify(
            s => s.PreserveOwnedAreasInHumanAsync(
                UserId,
                It.Is<IReadOnlyCollection<string>>(c => c.Contains("downtown") && c.Contains("my park"))),
            Times.Once);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["downtown", "my park"], Assert.IsType<string[]>(ok.Value));
    }

    private static PoracleHumanProxy CreateProxy(Scripted handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret",
            })
            .Build(),
        PoracleHumanProxyTests.V2Schema(new PoracleV2Capabilities { Read = true }),
        PoracleHumanProxyTests.AreaSecurityPolicy(),
        Mock.Of<ILogger<PoracleHumanProxy>>());

    private sealed record Reply(string Body);

    private sealed record Sent(string Url, string? Body);

    private sealed class Scripted(params Reply[] replies) : HttpMessageHandler
    {
        private int _next;

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(new Sent(request.RequestUri!.ToString(), body));

            var reply = replies[Math.Min(this._next++, replies.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
