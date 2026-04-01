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
    public async Task GetHumanAsync_ReturnsJsonOn200()
    {
        var responseBody = """{"id":"user1","name":"TestUser","enabled":1}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("user1");

        Assert.NotNull(result);
        Assert.Equal("user1", result.Value.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetHumanAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHumanAsync_ReturnsNullOnOtherErrors()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetHumanAsync("user1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHumanAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"id":"u1"}""");
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
    public async Task CreateHumanAsync_SendsPostWithBody()
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
    public async Task CreateHumanAsync_ThrowsOnNon2xx()
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
    public async Task StartAsync_CallsCorrectEndpoint()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StartAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/start", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task StartAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.StartAsync("user1"));
    }

    [Fact]
    public async Task StopAsync_CallsCorrectEndpoint()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StopAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/stop", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task StopAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.StopAsync("user1"));
    }

    // ──────────────────────────────────────────────────────────────
    // AdminDisabledAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminDisabledAsync_SendsDisableBody()
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
    public async Task AdminDisabledAsync_SendsEnableBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.AdminDisabledAsync("user1", false);

        var sentBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("\"adminDisable\":0", sentBody);
    }

    [Fact]
    public async Task AdminDisabledAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.AdminDisabledAsync("user1", true));
    }

    // ──────────────────────────────────────────────────────────────
    // SetLocationAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLocationAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetLocationAsync("user1", 40.7128, -74.006);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("/api/humans/user1/setLocation/40.7128/-74.006", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task SetLocationAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetLocationAsync("user1", 0, 0));
    }

    // ──────────────────────────────────────────────────────────────
    // SetAreasAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAreasAsync_SendsAreaArray()
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
    public async Task SetAreasAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetAreasAsync("user1", ["area1"]));
    }

    // ──────────────────────────────────────────────────────────────
    // GetAreasAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAreasAsync_ReturnsJsonOn200()
    {
        var responseBody = """{"area":["downtown","west end"]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetAreasAsync("user1");

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("area", out var areas));
        Assert.Equal(2, areas.GetArrayLength());
    }

    [Fact]
    public async Task GetAreasAsync_ReturnsNullOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetAreasAsync("user1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAreasAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.GetAreasAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1", handler.LastRequest.RequestUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────
    // SwitchProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchProfileAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SwitchProfileAsync("user1", 3);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/humans/user1/switchProfile/3", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task SwitchProfileAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SwitchProfileAsync("user1", 1));
    }

    // ──────────────────────────────────────────────────────────────
    // GetProfilesAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfilesAsync_ReturnsJsonResponse()
    {
        var responseBody = """[{"profileNo":1,"name":"default"},{"profileNo":2,"name":"alt"}]""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetProfilesAsync("user1");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(2, result.GetArrayLength());
    }

    [Fact]
    public async Task GetProfilesAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var sut = CreateSut(handler);

        await sut.GetProfilesAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetProfilesAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetProfilesAsync("user1"));
    }

    // ──────────────────────────────────────────────────────────────
    // AddProfileAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddProfileAsync_CallsCorrectUrlWithBody()
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
    public async Task AddProfileAsync_ThrowsOnNon2xx()
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
    public async Task UpdateProfileAsync_CallsCorrectUrlWithBody()
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
    public async Task UpdateProfileAsync_ThrowsOnNon2xx()
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
    public async Task DeleteProfileAsync_CallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteProfileAsync("user1", 2);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/profiles/user1/byProfileNo/2", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteProfileAsync_ThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DeleteProfileAsync("user1", 1));
    }

    // ──────────────────────────────────────────────────────────────
    // CheckLocationAsync
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckLocationAsync_ReturnsJsonOn200()
    {
        var responseBody = """{"areas":["downtown"]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.CheckLocationAsync("user1", 40.7128, -74.006);

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("areas", out _));
    }

    [Fact]
    public async Task CheckLocationAsync_ReturnsNullOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{}");
        var sut = CreateSut(handler);

        var result = await sut.CheckLocationAsync("user1", 0, 0);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckLocationAsync_CallsCorrectUrl()
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
    public async Task AllRequests_IncludePoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"id":"u1"}""");
        var sut = CreateSut(handler);

        await sut.GetHumanAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
        Assert.Equal(ApiSecret, handler.LastRequest.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task AllRequests_OmitSecretHeaderWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"id":"u1"}""");
        var sut = CreateSut(handler, CreateConfigNoSecret());

        await sut.GetHumanAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task StartAsync_IncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.StartAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task SetAreasAsync_IncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetAreasAsync("user1", ["area"]);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task DeleteProfileAsync_IncludesPoracleSecretHeader()
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

    private class MockHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

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
