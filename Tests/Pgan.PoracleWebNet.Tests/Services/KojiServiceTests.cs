using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class KojiServiceTests
{
    private const string ApiAddress = "http://localhost:8080";

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koji:ApiAddress"] = ApiAddress,
            ["Koji:ProjectId"] = "5",
            ["Koji:ProjectName"] = "PoracleJS"
        })
        .Build();

    private static KojiService CreateSut(CapturingHandler handler) =>
        new(new HttpClient(handler), CreateConfig(), new MemoryCache(new MemoryCacheOptions()), NullLogger<KojiService>.Instance);

    private static readonly double[][] s_polygon = [[1.0, 2.0], [3.0, 4.0], [5.0, 6.0]];

    // Koji resolves __parent as a geofence id. parentId 0 (a region-less geofence, issue #314) must be
    // serialized as JSON null, otherwise Koji returns HTTP 500 "[GEOFENCE]: Does not exist".
    [Fact]
    public async Task SaveGeofenceAsyncSendsNullParentWhenParentIdIsZero()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", string.Empty, parentId: 0, polygon: s_polygon, isPublic: true);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Null, parent.ValueKind);
    }

    [Fact]
    public async Task SaveGeofenceAsyncSendsNullParentWhenParentIdNegative()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", string.Empty, parentId: -1, polygon: s_polygon);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Null, parent.ValueKind);
    }

    [Fact]
    public async Task SaveGeofenceAsyncSendsNumericParentWhenParentIdPositive()
    {
        var handler = new CapturingHandler();
        var sut = CreateSut(handler);

        await sut.SaveGeofenceAsync("downtown", "Downtown", "City", parentId: 42, polygon: s_polygon, isPublic: true);

        var parent = GetParentProperty(handler.LastBody!);
        Assert.Equal(JsonValueKind.Number, parent.ValueKind);
        Assert.Equal(42, parent.GetInt32());
    }

    private static JsonElement GetParentProperty(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var properties = doc.RootElement
            .GetProperty("area")
            .GetProperty("features")[0]
            .GetProperty("properties");
        return properties.GetProperty("__parent").Clone();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
