using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PoracleSummaryProxyTests
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

    private static PoracleSummaryProxy CreateSut(MockHttpMessageHandler handler, IConfiguration? config = null)
    {
        var client = new HttpClient(handler);
        return new PoracleSummaryProxy(client, config ?? CreateConfig());
    }

    // ──────────────────────────────────────────────────────────────
    // GetSchedulesAsync — unwraps { "schedules": [...] }
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSchedulesAsyncUnwrapsSchedulesArrayOn200()
    {
        var responseBody = /*lang=json,strict*/ """{"status":"ok","schedules":[{"id":"user1","alert_type":"quest","active_hours":"[]"}]}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetSchedulesAsync("user1");

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Array, result.Value.ValueKind);
        Assert.Equal(1, result.Value.GetArrayLength());
        Assert.Equal("quest", result.Value[0].GetProperty("alert_type").GetString());
    }

    [Fact]
    public async Task GetSchedulesAsyncReturnsNullOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetSchedulesAsync("user1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSchedulesAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"schedules":[]}""");
        var sut = CreateSut(handler);

        await sut.GetSchedulesAsync("user42");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/summaries/user42", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetSchedulesAsyncThrowsBackendUnavailableOn503()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, /*lang=json,strict*/ """{"status":"error","message":"store not constructed"}""");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<SummaryBackendUnavailableException>(() => sut.GetSchedulesAsync("user1"));
    }

    // ──────────────────────────────────────────────────────────────
    // GetScheduleAsync — unwraps { "schedule": {...} }; 404 -> null
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleAsyncUnwrapsScheduleObjectOn200()
    {
        var responseBody = /*lang=json,strict*/ """{"status":"ok","schedule":{"id":"user1","alert_type":"quest","active_hours":"[{\"day\":1,\"hours\":9,\"mins\":0}]"}}""";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var sut = CreateSut(handler);

        var result = await sut.GetScheduleAsync("user1", "quest");

        Assert.NotNull(result);
        Assert.Equal("quest", result.Value.GetProperty("alert_type").GetString());
    }

    [Fact]
    public async Task GetScheduleAsyncReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, /*lang=json,strict*/ """{"status":"error","message":"schedule not found"}""");
        var sut = CreateSut(handler);

        var result = await sut.GetScheduleAsync("user1", "quest");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetScheduleAsyncReturnsNullOnOtherNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "{}");
        var sut = CreateSut(handler);

        var result = await sut.GetScheduleAsync("user1", "quest");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetScheduleAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"schedule":{}}""");
        var sut = CreateSut(handler);

        await sut.GetScheduleAsync("user1", "quest");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/summaries/user1/quest", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetScheduleAsyncThrowsBackendUnavailableOn503()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<SummaryBackendUnavailableException>(() => sut.GetScheduleAsync("user1", "quest"));
    }

    // ──────────────────────────────────────────────────────────────
    // SetScheduleAsync — POST { "active_hours": <raw> }; upsert
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetScheduleAsyncSendsPostWithRawActiveHoursBody()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var activeHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0}]";
        await sut.SetScheduleAsync("user1", "quest", activeHours);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/summaries/user1/quest", handler.LastRequest.RequestUri?.ToString());

        var sentBody = await handler.LastRequest.Content!.ReadAsStringAsync();
        // Raw JSON array literal embedded directly — NOT snake_case re-serialized, NOT escaped as a string.
        Assert.Equal(/*lang=json,strict*/ "{\"active_hours\":[{\"day\":1,\"hours\":9,\"mins\":0}]}", sentBody);
    }

    [Fact]
    public async Task SetScheduleAsyncEmptyOrWhitespaceCoercesToEmptyArrayLiteral()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.SetScheduleAsync("user1", "quest", "   ");

        var sentBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Equal(/*lang=json,strict*/ "{\"active_hours\":[]}", sentBody);
    }

    [Fact]
    public async Task SetScheduleAsyncThrowsOnNon2xx()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.BadRequest, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SetScheduleAsync("user1", "quest", "[]"));
    }

    [Fact]
    public async Task SetScheduleAsyncThrowsBackendUnavailableOn503()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<SummaryBackendUnavailableException>(() => sut.SetScheduleAsync("user1", "quest", "[]"));
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteScheduleAsync — idempotent
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteScheduleAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.DeleteScheduleAsync("user1", "quest");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Delete, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/summaries/user1/quest", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task DeleteScheduleAsyncSucceedsOn200ForMissingSchedule()
    {
        // Upstream returns 200 ok for deleting a missing schedule (idempotent) — proxy must not throw.
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"status":"ok"}""");
        var sut = CreateSut(handler);

        await sut.DeleteScheduleAsync("user1", "quest");
    }

    [Fact]
    public async Task DeleteScheduleAsyncThrowsBackendUnavailableOn503()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<SummaryBackendUnavailableException>(() => sut.DeleteScheduleAsync("user1", "quest"));
    }

    // ──────────────────────────────────────────────────────────────
    // TriggerAsync — flush-and-deliver-now
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerAsyncCallsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.TriggerAsync("user1", "quest");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal($"{ApiAddress}/api/summaries/user1/quest/trigger", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task TriggerAsyncThrowsBackendUnavailableOn503()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<SummaryBackendUnavailableException>(() => sut.TriggerAsync("user1", "quest"));
    }

    // ──────────────────────────────────────────────────────────────
    // userId encoding (webhook-style ids contain slashes)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleAsyncEncodesUserIdInPath()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"schedule":{}}""");
        var sut = CreateSut(handler);

        await sut.GetScheduleAsync("http://hook:1/abc", "quest");

        Assert.NotNull(handler.LastRequest);
        var url = handler.LastRequest.RequestUri?.ToString();
        Assert.NotNull(url);
        // The raw userId slashes/colon must be percent-encoded so they don't become path segments.
        Assert.DoesNotContain("/api/summaries/http://hook:1/abc/quest", url);
        Assert.Contains("http%3A%2F%2Fhook%3A1%2Fabc", url);
    }

    // ──────────────────────────────────────────────────────────────
    // Auth header (X-Poracle-Secret)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestsIncludePoracleSecretHeaderWhenConfigured()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"schedules":[]}""");
        var sut = CreateSut(handler);

        await sut.GetSchedulesAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
        Assert.Equal(ApiSecret, handler.LastRequest.Headers.GetValues("X-Poracle-Secret").Single());
    }

    [Fact]
    public async Task RequestsOmitSecretHeaderWhenEmpty()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"schedules":[]}""");
        var sut = CreateSut(handler, CreateConfigNoSecret());

        await sut.GetSchedulesAsync("user1");

        Assert.NotNull(handler.LastRequest);
        Assert.False(handler.LastRequest.Headers.Contains("X-Poracle-Secret"));
    }

    [Fact]
    public async Task TriggerIncludesPoracleSecretHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        await sut.TriggerAsync("user1", "quest");

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
