using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Parsing of the upstream disable signals and the alert-language allow-list: the <c>disabledHooks</c>
/// array and <c>availableLanguages</c> on <c>/api/config/poracleWeb</c>, and the booleans read from
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
            .Build(),
        PoracleHumanProxyTests.V2Schema());

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

    // --- availableLanguages: absent and null both restrict nothing; a list is the allow-list ---

    /// <summary>
    /// Verified against a live 5.1.0: the key is absent. That reads as unrestricted, like null.
    /// </summary>
    [Fact]
    public async Task AbsentAvailableLanguagesRestrictsNothing()
    {
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"locale":"en"}"""));

        var config = await sut.GetConfigAsync();

        Assert.Null(config?.AvailableLanguages);
    }

    /// <summary>
    /// Verified against a live 5.2.1: present and null, meaning unrestricted. Upstream reports null for
    /// both an unset and an empty map, because its own write path only validates a non-empty one.
    /// </summary>
    [Fact]
    public async Task NullAvailableLanguagesRestrictsNothing()
    {
        var sut = CreateSut(new MockHttpMessageHandler(HttpStatusCode.OK, /*lang=json,strict*/ """{"availableLanguages":null}"""));

        var config = await sut.GetConfigAsync();

        Assert.Null(config?.AvailableLanguages);
    }

    [Fact]
    public async Task AvailableLanguagesArrayIsParsedAsTheExhaustiveAllowList()
    {
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"availableLanguages":["en","de","pt-BR"]}"""));

        var config = await sut.GetConfigAsync();

        Assert.Equal(["en", "de", "pt-BR"], config?.AvailableLanguages);
    }

    [Fact]
    public async Task QuestSummaryFlagStillReadsFromTheTrackingSection()
    {
        // The two config-values reads share one helper; this is the sibling that already existed.
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"tracking":{"quest_summary_enabled":true}}}"""));

        Assert.True(await sut.GetQuestSummaryEnabledAsync());
    }

    /// <summary>
    /// Shape verified live against a PoracleNG build carrying jfberry/PoracleNG#246 (dev-01's "next"
    /// testbed, rebuilt 2026-10-14 to commit 4931758f): <c>GET /api/config/values</c> returns
    /// <c>tracking.pokemon_edit: true</c>.
    /// </summary>
    [Fact]
    public async Task PokemonEditFlagReadsFromTheTrackingSection()
    {
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"tracking":{"pokemon_edit":true}}}"""));

        Assert.True(await sut.GetPokemonEditEnabledAsync());
    }

    /// <summary>
    /// A PoracleNG built before #246 has no such key: the processor accepted the pokemon edit bit but
    /// never acted on it, so there was nothing to configure. Null means "cannot determine", which the
    /// capability service must not read as "enabled".
    /// </summary>
    [Fact]
    public async Task AbsentPokemonEditFlagReturnsNull()
    {
        var sut = CreateSut(new MockHttpMessageHandler(
            HttpStatusCode.OK, /*lang=json,strict*/ """{"values":{"tracking":{}}}"""));

        Assert.Null(await sut.GetPokemonEditEnabledAsync());
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
