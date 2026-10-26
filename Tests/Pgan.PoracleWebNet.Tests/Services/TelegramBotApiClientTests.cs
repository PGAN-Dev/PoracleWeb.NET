using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// <c>getChatMember</c> over the Bot API. Statuses follow PoracleNG's reconciliation (a member is anyone who has not
/// left or been banned), except a <c>restricted</c> user whose <c>is_member</c> is explicitly false, who is outside
/// the chat. Every failure is "not a member", and the bot token never reaches a log line. See #1056.
/// </summary>
public class TelegramBotApiClientTests
{
    private const string Token = "123456:SECRET-TOKEN-VALUE";

    private static (TelegramBotApiClient Sut, CapturingLogger Log, List<HttpRequestMessage> Requests) Create(
        Func<HttpRequestMessage, HttpResponseMessage> answer, string token = Token)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new Delegating(r =>
        {
            requests.Add(r);
            return answer(r);
        });
        var log = new CapturingLogger();
        var sut = new TelegramBotApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = token }).Build(),
            log);
        return (sut, log, requests);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("member", true)]
    [InlineData("administrator", true)]
    [InlineData("creator", true)]
    [InlineData("left", false)]
    [InlineData("kicked", false)]
    [InlineData("something-new", false)]
    public async Task Status_DecidesMembership(string status, bool expected)
    {
        var (sut, _, _) = Create(_ => Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"status\":\"" + status + "\"}}"));

        Assert.Equal(expected, await sut.IsMemberAsync(-1001, 200000001));
    }

    [Theory]
    [InlineData("""{"ok":true,"result":{"status":"restricted","is_member":true}}""", true)]
    [InlineData("""{"ok":true,"result":{"status":"restricted"}}""", true)]
    [InlineData("""{"ok":true,"result":{"status":"restricted","is_member":false}}""", false)]
    public async Task Restricted_FollowsIsMember(string body, bool expected)
    {
        var (sut, _, _) = Create(_ => Json(HttpStatusCode.OK, body));

        Assert.Equal(expected, await sut.IsMemberAsync(-1001, 200000001));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: user not found"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"ok":false,"error_code":403,"description":"Forbidden: bot was kicked from the group chat"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"ok":false,"error_code":429}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"ok":false}""")]
    [InlineData(HttpStatusCode.OK, """{"ok":true,"result":null}""")]
    public async Task TelegramRefusing_IsNotAMember_NeverAnException(HttpStatusCode status, string body)
    {
        var (sut, _, _) = Create(_ => Json(status, body));

        Assert.False(await sut.IsMemberAsync(-1001, 200000001));
    }

    [Fact]
    public async Task ANetworkFailure_IsNotAMember_AndTheLogsNameNeitherTheTokenNorTheUrl()
    {
        var (sut, log, _) = Create(r => throw new HttpRequestException($"connection refused for {r.RequestUri}"));

        Assert.False(await sut.IsMemberAsync(-1001, 200000001));

        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, l =>
        {
            Assert.DoesNotContain("SECRET-TOKEN-VALUE", l);
            Assert.DoesNotContain("api.telegram.org", l);
        });
    }

    [Fact]
    public async Task AnAnswerThatEchoesTheToken_IsNotLogged()
    {
        var (sut, log, _) = Create(r => Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"bad " + r.RequestUri + "\"}"));

        await sut.IsMemberAsync(-1001, 200000001);

        Assert.All(log.Lines, l => Assert.DoesNotContain("SECRET-TOKEN-VALUE", l));
    }

    [Fact]
    public async Task NoToken_AsksNobodyAndIsNotAMember()
    {
        var (sut, _, requests) = Create(_ => Json(HttpStatusCode.OK, """{"ok":true,"result":{"status":"member"}}"""), token: "");

        Assert.False(await sut.IsMemberAsync(-1001, 200000001));
        Assert.Empty(requests);
    }

    [Fact]
    public async Task TheRequest_NamesTheChatAndTheUser()
    {
        var (sut, _, requests) = Create(_ => Json(HttpStatusCode.OK, """{"ok":true,"result":{"status":"member"}}"""));

        await sut.IsMemberAsync(-1001234, 200000001);

        var uri = Assert.Single(requests).RequestUri!.ToString();
        Assert.Contains("bot" + Token + "/getChatMember", uri);
        Assert.Contains("chat_id=-1001234", uri);
        Assert.Contains("user_id=200000001", uri);
    }

    private sealed class Delegating(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    public sealed class CapturingLogger : ILogger<TelegramBotApiClient>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
    }
}
