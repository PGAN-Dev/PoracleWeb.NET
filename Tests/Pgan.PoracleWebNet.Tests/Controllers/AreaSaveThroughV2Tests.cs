using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Tests.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The Areas page save end to end through a real <see cref="PoracleHumanProxy"/> and <see cref="UserAreaWriter"/>:
/// one trusted POST and nothing else written, a user-drawn fence included, and the response is the stored
/// list read back. See #995, #838.
/// </summary>
public class AreaSaveThroughV2Tests : ControllerTestBase
{
    private const string ApiAddress = "http://localhost:3030";
    private const string UserId = "123456789";

    [Fact]
    public async Task AnOwnFenceAndAnAdminAreaAreSavedInOneTrustedPost()
    {
        var handler = new Scripted(
            // The list the profile holds now.
            new Reply("""{"human":{"id":"123456789","area":[],"current_profile_no":1}}"""),
            // The fences PoracleNG offers this human.
            new Reply("""{"areas":[{"name":"downtown","userSelectable":true,"exemptFromAreaSecurity":false}]}"""),
            // The trusted write.
            new Reply("""{"areas":["downtown","my park"],"rejected":[]}"""),
            // The read-back.
            new Reply("""{"human":{"id":"123456789","area":["downtown","my park"],"current_profile_no":1}}"""));

        var fences = new Mock<IUserGeofenceRepository>();
        fences.Setup(r => r.GetByHumanIdAsync(UserId))
            .ReturnsAsync([new UserGeofence { Id = 1, HumanId = UserId, KojiName = "my park" }]);

        var proxy = CreateProxy(handler);
        var sut = new AreaController(
            proxy,
            Mock.Of<IPoracleApiProxy>(),
            Mock.Of<IUserGeofenceService>(),
            new UserAreaWriter(proxy, fences.Object, Mock.Of<IPoracleTrackingProxy>(), Mock.Of<ILogger<UserAreaWriter>>()),
            Mock.Of<ILogger<AreaController>>());
        SetupUser(sut, UserId);

        var result = await sut.UpdateAreas(new AreaController.UpdateAreasRequest { Areas = ["Downtown", "My Park"] });

        var writes = handler.Requests.Where(r => r.Method != HttpMethod.Get).ToList();
        var save = Assert.Single(writes);
        Assert.Equal($"{ApiAddress}/api/v2/humans/{UserId}/areas", save.Url);

        using var body = JsonDocument.Parse(save.Body!);
        var sentAreas = body.RootElement.GetProperty("areas");
        Assert.Equal(["downtown", "my park"], sentAreas.EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.True(body.RootElement.GetProperty("trusted").GetBoolean());

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
        Mock.Of<ILogger<PoracleHumanProxy>>());

    private sealed record Reply(string Body);

    private sealed record Sent(HttpMethod Method, string Url, string? Body);

    private sealed class Scripted(params Reply[] replies) : HttpMessageHandler
    {
        private int _next;

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Requests.Add(new Sent(request.Method, request.RequestUri!.ToString(), body));

            var reply = replies[Math.Min(this._next++, replies.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
