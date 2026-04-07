using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PoracleHumanProxyTests
{
    private const string ApiAddress = "http://localhost:3030";
    private const string ApiSecret = "test-secret";

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Poracle:ApiAddress"] = ApiAddress,
            ["Poracle:ApiSecret"] = ApiSecret
        })
        .Build();

    private static IConfiguration CreateConfigNoSecret() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Poracle:ApiAddress"] = ApiAddress,
            ["Poracle:ApiSecret"] = ""
        })
        .Build();

    private static PoracleHumanProxy CreateSut(MockHttpMessageHandler handler, IConfiguration? config = null)
    {
        var client = new HttpClient(handler);
        return new PoracleHumanProxy(client, config ?? CreateConfig());
    }

    // ──────────────────────────────────────────────────────────────
    // GetHumanAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHumanAsyncReturnsJsonOn200()
    {
        var responseBody = /*lang=json,strict*/ """{"id":"user1","name":"TestUser","enabled":1}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("user1");

        Assert.NotNull(result);
        Assert.Equal("user1", result.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetHumanAsyncReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHumanAsyncReturnsNullOnOtherErrors()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("user1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHumanAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"id":"u1"}""");
        var sut = CreateSut(handler);

        await sut.GetHumanAsync("user42");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/one/user42", handler.LastRequest.RequestUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // CreateHumanAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateHumanAsyncSendsPostWithBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("""{"id":"newuser","name":"New"}""").RootElement;
        await sut.CreateHumanAsync(body);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("newuser", sentBody);
    }

    [Fact]
    public async Task CreateHumanAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Conflict, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.CreateHumanAsync(body));
    }

    // ──────────────────────────────────────────────────────────────
    // StartAsync / StopAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncCallsCorrectEndpoint()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StartAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/start", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task StartAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.StartAsync("user1"));
    }

    [Fact]
    public async Task StopAsyncCallsCorrectEndpoint()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StopAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/stop", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task StopAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.StopAsync("user1"));
    }

    // ──────────────────────────────────────────────────────────────
    // AdminDisabledAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminDisabledAsyncSendsDisableBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.AdminDisabledAsync("user1", true);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/adminDisabled", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"adminDisable\":1", sentBody);
    }

    [Fact]
    public async Task AdminDisabledAsyncSendsEnableBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.AdminDisabledAsync("user1", false);

        var sentBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("\"adminDisable\":0", sentBody);
    }

    [Fact]
    public async Task AdminDisabledAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.AdminDisabledAsync("user1", true));
    }

    // ──────────────────────────────────────────────────────────────
    // SetLocationAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLocationAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetLocationAsync("user1", 40.7128, -74.006);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("/api/humans/user1/setLocation/40.7128/-74.006", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task SetLocationAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetLocationAsync("user1", 0, 0));
    }

    // ──────────────────────────────────────────────────────────────
    // SetAreasAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAreasAsyncSendsAreaArray()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetAreasAsync("user1", ["downtown", "west end"]);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/setAreas", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("downtown", sentBody);
        Assert.Contains("west end", sentBody);
    }

    [Fact]
    public async Task SetAreasAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetAreasAsync("user1", ["area1"]));
    }

    // ──────────────────────────────────────────────────────────────
    // GetAreasAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAreasAsyncReturnsJsonOn200()
    {
        var responseBody = /*lang=json,strict*/ """{"area":["downtown","west end"]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetAreasAsync("user1");

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("area", out var areas));
        Assert.Equal(2, areas.GetArrayLength());
    }

    [Fact]
    public async Task GetAreasAsyncReturnsNullOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetAreasAsync("user1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAreasAsyncCallsCorrectUrl()
    {
        // GetAreasAsync delegates to GetHumanAsync which calls /api/humans/one/{id}
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"human":{"id":"user1","area":"[]"}}""");
        var sut = CreateSut(handler);

        await sut.GetAreasAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/one/user1", handler.LastRequest.RequestUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // SwitchProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchProfileAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SwitchProfileAsync("user1", 3);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/switchProfile/3", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task SwitchProfileAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SwitchProfileAsync("user1", 1));
    }

    // ──────────────────────────────────────────────────────────────
    // GetProfilesAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfilesAsyncReturnsJsonResponse()
    {
        var responseBody = /*lang=json,strict*/ """[{"profileNo":1,"name":"default"},{"profileNo":2,"name":"alt"}]""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetProfilesAsync("user1");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(2, result.GetArrayLength());
    }

    [Fact]
    public async Task GetProfilesAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var sut = CreateSut(handler);

        await sut.GetProfilesAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetProfilesAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetProfilesAsync("user1"));
    }

    // ──────────────────────────────────────────────────────────────
    // AddProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddProfileAsyncCallsCorrectUrlWithBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("""{"name":"new profile"}""").RootElement;
        await sut.AddProfileAsync("user1", body);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/add", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("new profile", sentBody);
    }

    [Fact]
    public async Task AddProfileAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.AddProfileAsync("user1", body));
    }

    // ──────────────────────────────────────────────────────────────
    // UpdateProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsyncCallsCorrectUrlWithBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("""{"profileNo":2,"name":"renamed"}""").RootElement;
        await sut.UpdateProfileAsync("user1", body);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/update", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task UpdateProfileAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var body = JsonDocument.Parse("{}").RootElement;
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.UpdateProfileAsync("user1", body));
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProfileAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteProfileAsync("user1", 2);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/byProfileNo/2", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteProfileAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DeleteProfileAsync("user1", 1));
    }

    // ──────────────────────────────────────────────────────────────
    // CheckLocationAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckLocationAsyncReturnsJsonOn200()
    {
        var responseBody = /*lang=json,strict*/ """{"areas":["downtown"]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.CheckLocationAsync("user1", 40.7128, -74.006);

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("areas", out _));
    }

    [Fact]
    public async Task CheckLocationAsyncReturnsNullOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.CheckLocationAsync("user1", 0, 0);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckLocationAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.CheckLocationAsync("user1", 51.5, -0.12);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Contains("/api/humans/user1/checkLocation/51.5/-0.12", handler.LastRequest.RequestUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // Auth header
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllRequestsIncludePoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"id":"u1"}""");
        var sut = CreateSut(handler);

        await sut.GetHumanAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
        Assert.Equal(ApiSecret, handler.LastRequest.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task AllRequestsOmitSecretHeaderWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"id":"u1"}""");
        var sut = CreateSut(handler, CreateConfigNoSecret());

        await sut.GetHumanAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task StartAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StartAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task SetAreasAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetAreasAsync("user1", ["area"]);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task DeleteProfileAsyncIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteProfileAsync("user1", 1);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    // ──────────────────────────────────────────────────────────────
    // Mock handler
    // ──────────────────────────────────────────────────────────────

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
