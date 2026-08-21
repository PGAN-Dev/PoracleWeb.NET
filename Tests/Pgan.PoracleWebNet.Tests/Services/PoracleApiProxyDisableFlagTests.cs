using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Parsing of the two upstream disable signals: the <c>disabledHooks</c> array on
/// <c>/api/config/poracleWeb</c>, and <c>general.disable_fort_update</c> on
/// <c>/api/config/values</c>. See #769.
/// </summary>
public class PoracleApiProxyDisableFlagTests
{
    private const string ApiAddress = "http://localhost:3030";

    private static PoracleApiProxy CreateSut(MockHttpMessageHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = ApiAddress,
                ["Poracle:ApiSecret"] = "test-secret"
            })
            .Build());

    /// <summary>The body prod actually returns, trimmed to the field under test.</summary>
    [Fact]
    public async Task EmptyDisabledHooksParsesAsAnEmptyListNotNull()
    {
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"disabledHooks":[]}"""));

        var config = await sut.GetConfigAsync();

        // Empty and absent must stay distinguishable: empty means "upstream disables nothing",
        // absent means "upstream has no opinion". Only the caller gets to collapse them.
        Assert.NotNull(config?.DisabledHooks);
        Assert.Empty(config.DisabledHooks);
    }

    [Fact]
    public async Task DisabledHooksEntriesAreParsed()
    {
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"disabledHooks":["raid","quest","pokestop"]}"""));

        var config = await sut.GetConfigAsync();

        Assert.Equal(["raid", "quest", "pokestop"], config?.DisabledHooks);
    }

    [Fact]
    public async Task AbsentDisabledHooksLeavesTheListNull()
    {
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"locale":"en"}"""));

        var config = await sut.GetConfigAsync();

        Assert.NotNull(config);
        Assert.Null(config.DisabledHooks);
    }

    [Fact]
    public async Task NonArrayDisabledHooksLeavesTheListNull()
    {
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"disabledHooks":null}"""));

        Assert.Null((await sut.GetConfigAsync())?.DisabledHooks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FortUpdateDisabledIsReadFromTheGeneralSection(bool disabled)
    {
        var body = """{"values":{"general":{"disable_fort_update":VALUE}}}"""
            .Replace("VALUE", disabled ? "true" : "false", StringComparison.Ordinal);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, body);
        var sut = CreateSut(handler);

        Assert.Equal(disabled, await sut.GetFortUpdateDisabledAsync());
        Assert.Equal($"{ApiAddress}/api/config/values", handler.LastRequest?.RequestUri?.ToString());
    }

    /// <summary>
    /// PoracleJS and older PoracleNG builds do not carry the key. Null means "cannot determine",
    /// which the caller must not read as "disabled".
    /// </summary>
    [Fact]
    public async Task AbsentFortUpdateFlagReturnsNull()
    {
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"general":{}}}"""));

        Assert.Null(await sut.GetFortUpdateDisabledAsync());
    }

    [Fact]
    public async Task QuestSummaryFlagStillReadsFromTheTrackingSection()
    {
        // The two config-values reads share one helper; this is the sibling that already existed.
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"tracking":{"quest_summary_enabled":true}}}"""));

        Assert.True(await sut.GetQuestSummaryEnabledAsync());
    }

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
