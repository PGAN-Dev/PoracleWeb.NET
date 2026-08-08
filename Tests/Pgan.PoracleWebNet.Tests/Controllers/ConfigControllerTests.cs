using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ConfigControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleApiProxy> _proxy = new();
    private readonly Mock<ILogger<ConfigController>> _logger = new();
    private readonly ConfigController _sut;

    public ConfigControllerTests()
    {
        this._sut = new ConfigController(this._proxy.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    // --- GetTemplates ---

    [Fact]
    public async Task GetTemplatesReturnsContentWhenAvailable()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync(/*lang=json,strict*/ "{\"templates\":{}}");

        var result = await this._sut.GetTemplates();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task GetTemplatesReturnsNullBranchReturnsFallback()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ReturnsAsync((string?)null);

        var result = await this._sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetTemplatesReturnsOkFallbackWhenExceptionThrown()
    {
        this._proxy.Setup(p => p.GetTemplatesAsync()).ThrowsAsync(new HttpRequestException("fail"));

        var result = await this._sut.GetTemplates();

        Assert.IsType<OkObjectResult>(result);
    }

    // --- GetConfig ---

    [Fact]
    public async Task GetConfigReturnsOkWhenAvailable()
    {
        var config = new PoracleConfig { Locale = "en", MaxDistance = 5000 };
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(config);

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PublicPoracleConfig>(ok.Value);
        Assert.Equal("en", returned.Locale);
        Assert.Equal(5000, returned.MaxDistance);
    }

    [Fact]
    public async Task GetConfigSurfacesPvpCapsAndDefaultPvpCap()
    {
        var config = new PoracleConfig
        {
            Locale = "en",
            PvpCaps = [50, 51],
            DefaultPvpCap = 50,
        };
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(config);

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PublicPoracleConfig>(ok.Value);
        Assert.Equal(new[] { 50, 51 }, returned.PvpCaps);
        Assert.Equal(50, returned.DefaultPvpCap);
    }

    [Fact]
    public async Task GetConfigReturnsFallbackConfigWhenNull()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PublicPoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
        Assert.Equal("unknown", config.PoracleVersion);
        Assert.Equal(10726000, config.MaxDistance);
    }

    [Fact]
    public async Task GetConfigReturnsFallbackConfigWhenExceptionThrown()
    {
        this._proxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new InvalidOperationException("fail"));

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<PublicPoracleConfig>(ok.Value);
        Assert.Equal("en", config.Locale);
    }

    // --- GetConfig: disclosure guards (#415) ---

    [Fact]
    public async Task GetConfigDoesNotExposeAdminIdsOrDelegationOrKeys()
    {
        // The proxy hands back everything upstream sends. The controller must project it down --
        // these four fields used to be served to anonymous callers verbatim.
        this._proxy.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig
        {
            Locale = "en",
            ProviderUrl = "http://internal-host:4000",
            StaticKey = "secret-static-key",
            Admins = new PoracleAdmins { Discord = ["111", "222"], Telegram = ["333"] },
            DelegateAdministration = [new PoracleDelegateEntry { WebhookId = "http://hook", DiscordIds = ["444"] }]
        });

        var result = await this._sut.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<PublicPoracleConfig>(ok.Value);

        // Assert on the serialized payload, because that is what actually reaches the browser.
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("222", json, StringComparison.Ordinal);
        Assert.DoesNotContain("333", json, StringComparison.Ordinal);
        Assert.DoesNotContain("444", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-static-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-host", json, StringComparison.Ordinal);
        Assert.DoesNotContain("admins", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delegate", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetConfigRequiresAuthenticationWhileTemplatesAndDtsStayAnonymous()
    {
        // GetConfig inherits [Authorize] from BaseApiController; the regression is someone
        // re-adding [AllowAnonymous] to it. The sibling routes are genuinely pre-auth.
        static bool Anonymous(string method) => typeof(ConfigController)
            .GetMethod(method)!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
            .Length > 0;

        Assert.False(Anonymous(nameof(ConfigController.GetConfig)));
        Assert.True(Anonymous(nameof(ConfigController.GetTemplates)));
        Assert.True(Anonymous(nameof(ConfigController.GetDts)));
    }

    [Fact]
    public void PublicProjectionCopiesEveryFieldTheBrowserUses()
    {
        var full = new PoracleConfig
        {
            Locale = "de",
            PoracleVersion = "1.2.3",
            PvpFilterMaxRank = 42,
            PvpFilterLittleMinCp = 1,
            PvpFilterGreatMinCp = 2,
            PvpFilterUltraMinCp = 3,
            PvpLittleLeagueAllowed = false,
            PvpCaps = [50, 51],
            DefaultPvpCap = 51,
            DefaultTemplateName = "custom",
            EverythingFlagPermissions = "everyone",
            MaxDistance = 1234
        };

        var projected = PublicPoracleConfig.From(full);

        Assert.Equal("de", projected.Locale);
        Assert.Equal("1.2.3", projected.PoracleVersion);
        Assert.Equal(42, projected.PvpFilterMaxRank);
        Assert.Equal(1, projected.PvpFilterLittleMinCp);
        Assert.Equal(2, projected.PvpFilterGreatMinCp);
        Assert.Equal(3, projected.PvpFilterUltraMinCp);
        Assert.False(projected.PvpLittleLeagueAllowed);
        Assert.Equal(new[] { 50, 51 }, projected.PvpCaps);
        Assert.Equal(51, projected.DefaultPvpCap);
        Assert.Equal("custom", projected.DefaultTemplateName);
        Assert.Equal("everyone", projected.EverythingFlagPermissions);
        Assert.Equal(1234, projected.MaxDistance);
    }

    // --- GetDts ---

    [Fact]
    public void GetDtsReturnsOkEmptyArrayWhenCacheEmpty()
    {
        // DtsCacheService.GetCachedDts() returns null/empty when not configured
        var result = this._sut.GetDts();
        Assert.IsType<OkObjectResult>(result);
    }
}
