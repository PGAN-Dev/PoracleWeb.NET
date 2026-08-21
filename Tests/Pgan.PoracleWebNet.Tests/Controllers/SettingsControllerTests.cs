using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class SettingsControllerTests : ControllerTestBase
{
    private readonly Mock<ISiteSettingService> _siteService = new();
    private readonly Mock<IUpstreamFeatureFlagService> _upstreamFlags = new();
    private readonly Mock<IPoracleApiProxy> _poracleApi = new();
    private readonly SettingsController _sut;

    public SettingsControllerTests()
    {
        this._poracleApi.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null);
        this._sut = this.CreateController();
    }

    private SettingsController CreateController(
        DiscordSettings? discord = null,
        PoracleSettings? poracle = null) => new(
        this._siteService.Object,
        Options.Create(discord ?? new DiscordSettings()),
        Options.Create(poracle ?? new PoracleSettings()),
        Options.Create(new TelegramSettings()),
        Options.Create(new OidcSettings()),
        this._upstreamFlags.Object,
        new ConfigurationBuilder().Build(),
        this._poracleApi.Object,
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<SettingsController>.Instance);

    [Fact]
    public async Task GetAllReturnsOkForAdmin()
    {
        SetupUser(this._sut, isAdmin: true);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "custom_title", Value = "My App" },
            new() { Key = "api_secret", Value = "secret123" }
        ]);

        var result = await this._sut.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result);
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false);
        Assert.Equal(2, settings.Count());
    }

    [Fact]
    public async Task GetAllFiltersSensitiveKeysForNonAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "custom_title", Value = "My App" },
            new() { Key = "api_secret", Value = "secret123" },
            new() { Key = "telegram_bot_token", Value = "tok" }
        ]);

        var result = await this._sut.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result);
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).ToList();
        Assert.Single(settings);
        Assert.Equal("custom_title", settings[0].Key);
    }

    /// <summary>
    /// The real key names, not the placeholder ones. The old denylist contained the literal "scan_db",
    /// which matches none of these rows, and omitted cf_id/cf_secret entirely -- so a scanner-database
    /// password and a Cloudflare Access token were served to every authenticated non-admin session.
    /// </summary>
    [Theory]
    [InlineData("scan_dbhost")]
    [InlineData("scan_dbuser")]
    [InlineData("scan_dbpass")]
    [InlineData("scan_dbport")]
    [InlineData("scan_dbname")]
    [InlineData("cf_id")]
    [InlineData("cf_secret")]
    [InlineData("api_secret")]
    [InlineData("telegram_bot_token")]
    [InlineData("discord_client_secret")]
    [InlineData("discord_bot_token")]
    [InlineData("admin_channel_id")]
    public async Task GetAllWithholdsCredentialBearingKeysFromNonAdmins(string key)
    {
        SetupUser(this._sut, isAdmin: false);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "custom_title", Value = "My App" },
            new() { Key = key, Value = "s3cret" }
        ]);

        var settings = await this.GetAllKeysAsync();

        Assert.DoesNotContain(key, settings);
        Assert.Contains("custom_title", settings);
    }

    /// <summary>Allowlist semantics: a key nobody has classified is hidden rather than exposed.</summary>
    [Fact]
    public async Task GetAllHidesUnrecognisedKeysFromNonAdminsByDefault()
    {
        SetupUser(this._sut, isAdmin: false);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "some_future_integration_token", Value = "s3cret" },
            new() { Key = "site_name", Value = "PGAN" }
        ]);

        var settings = await this.GetAllKeysAsync();

        Assert.DoesNotContain("some_future_integration_token", settings);
        Assert.Contains("site_name", settings);
    }

    [Theory]
    [InlineData("disable_mons")]
    [InlineData("disable_user_geofences")]
    [InlineData("enable_discord")]
    [InlineData("enable_templates")]
    [InlineData("uicons_pkmn")]
    [InlineData("allowed_languages")]
    [InlineData("custom_title")]
    [InlineData("favicon_url")]
    [InlineData("header_logo_url")]
    [InlineData("hide_header_logo")]
    [InlineData("signup_url")]
    [InlineData("site_name")]
    public async Task GetAllStillServesTheKeysTheSpaNeedsToNonAdmins(string key)
    {
        SetupUser(this._sut, isAdmin: false);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync([new() { Key = key, Value = "v" }]);

        Assert.Contains(key, await this.GetAllKeysAsync());
    }

    [Fact]
    public async Task GetAllStillReturnsEverythingToAdmins()
    {
        SetupUser(this._sut, isAdmin: true);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "scan_dbpass", Value = "p" },
            new() { Key = "cf_secret", Value = "s" },
            new() { Key = "custom_title", Value = "t" }
        ]);

        var settings = await this.GetAllKeysAsync();

        Assert.Contains("scan_dbpass", settings);
        Assert.Contains("cf_secret", settings);
        Assert.Contains("custom_title", settings);
    }

    private async Task<List<string>> GetAllKeysAsync()
    {
        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetAll());
        return [.. Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).Select(s => s.Key!)];
    }

    /// <summary>
    /// The SPA picks its display language before login, where /api/config 401s (#426), so Poracle's locale
    /// has to ride out on the anonymous settings endpoint instead.
    /// </summary>
    [Fact]
    public async Task GetPublicServesPoraclesLocaleToAnonymousVisitors()
    {
        this._sut.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        this._poracleApi.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { Locale = "de" });
        this._siteService.Setup(s => s.GetPublicAsync()).ReturnsAsync([new() { Key = "custom_title", Value = "App" }]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPublic());
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).ToList();

        Assert.Contains(settings, s => s.Key == SettingsController.PoracleLocaleKey && s.Value == "de");
        Assert.Contains(settings, s => s.Key == "custom_title");
    }

    [Fact]
    public async Task GetAllServesPoraclesLocaleToNonAdmins()
    {
        SetupUser(this._sut, isAdmin: false);
        this._poracleApi.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { Locale = "de" });
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync([new() { Key = "custom_title", Value = "t" }]);

        Assert.Contains(SettingsController.PoracleLocaleKey, await this.GetAllKeysAsync());
    }

    /// <summary>Poracle being down must cost the caller nothing but the locale.</summary>
    [Fact]
    public async Task GetPublicStillServesTheStoredSettingsWhenPoracleIsUnreachable()
    {
        this._sut.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        this._poracleApi.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._siteService.Setup(s => s.GetPublicAsync()).ReturnsAsync([new() { Key = "custom_title", Value = "App" }]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPublic());
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).ToList();

        Assert.Contains(settings, s => s.Key == "custom_title");
        Assert.DoesNotContain(settings, s => s.Key == SettingsController.PoracleLocaleKey);
    }

    [Fact]
    public async Task GetPublicLetsAStoredRowWinOverPoraclesLocale()
    {
        this._sut.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        this._poracleApi.Setup(p => p.GetConfigAsync()).ReturnsAsync(new PoracleConfig { Locale = "de" });
        this._siteService.Setup(s => s.GetPublicAsync())
            .ReturnsAsync([new() { Key = SettingsController.PoracleLocaleKey, Value = "fr" }]);

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetPublic());
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).ToList();

        Assert.Single(settings);
        Assert.Equal("fr", settings[0].Value);
    }

    /// <summary>
    /// Locales this UI ships no translation for (ja, ru, zh-cn) pass the shape check deliberately -- the SPA
    /// matches them against its own language list and the allowed_languages filter, and falls back to en.
    /// </summary>
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("pt-BR")]
    [InlineData("zh-cn")]
    [InlineData("ja")]
    public void NormalizeLocaleKeepsAnythingShapedLikeALocaleTag(string locale) =>
        Assert.Equal(locale, SettingsController.NormalizeLocale(locale));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("en; DROP TABLE humans")]
    [InlineData("../../etc/passwd")]
    [InlineData("englishy")]
    public void NormalizeLocaleRejectsAnythingElse(string? locale) =>
        Assert.Null(SettingsController.NormalizeLocale(locale));

    [Fact]
    public async Task GetPublicReturnsOk()
    {
        this._sut.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        this._siteService.Setup(s => s.GetPublicAsync()).ReturnsAsync([new() { Key = "custom_title", Value = "App" }]);

        var result = await this._sut.GetPublic();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpsertReturnsOkWhenAdmin()
    {
        SetupUser(this._sut, isAdmin: true);
        var request = new SettingsController.SiteSettingRequest { Value = "val", Category = "branding" };
        this._siteService.Setup(s => s.GetByKeyAsync("key1")).ReturnsAsync((SiteSetting?)null);
        this._siteService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>())).ReturnsAsync(new SiteSetting { Key = "key1", Value = "val" });

        var result = await this._sut.Upsert("key1", request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpsertPreservesExistingValueType()
    {
        SetupUser(this._sut, isAdmin: true);
        this._siteService.Setup(s => s.GetByKeyAsync("enable_roles"))
            .ReturnsAsync(new SiteSetting { Key = "enable_roles", Value = "True", Category = "admin", ValueType = "boolean" });
        this._siteService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        var request = new SettingsController.SiteSettingRequest { Value = "False" };
        await this._sut.Upsert("enable_roles", request);

        this._siteService.Verify(s => s.CreateOrUpdateAsync(It.Is<SiteSetting>(ss =>
            ss.ValueType == "boolean" && ss.Category == "admin")), Times.Once);
    }

    [Fact]
    public async Task UpsertReturnsForbidWhenNotAdmin()
    {
        SetupUser(this._sut, isAdmin: false);
        var result = await this._sut.Upsert("key1", new SettingsController.SiteSettingRequest());
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAllFiltersInternalKeysForAdmin()
    {
        SetupUser(this._sut, isAdmin: true);
        this._siteService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Key = "custom_title", Value = "My App" },
            new() { Key = "migration_completed", Value = "true", Category = "system" }
        ]);

        var result = await this._sut.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result);
        var settings = Assert.IsType<IEnumerable<SiteSetting>>(ok.Value, exactMatch: false).ToList();
        Assert.Single(settings);
        Assert.Equal("custom_title", settings[0].Key);
    }

    [Fact]
    public async Task UpsertReturnsBadRequestForInternalKey()
    {
        SetupUser(this._sut, isAdmin: true);
        var request = new SettingsController.SiteSettingRequest { Value = "false" };

        var result = await this._sut.Upsert("migration_completed", request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    /// <summary>
    /// poracle_locale is synthesized from Poracle's config, and a real row would win over it, so a
    /// single accidental save would pin the language default and stop tracking Poracle for good.
    /// </summary>
    [Fact]
    public async Task UpsertRefusesToWriteThePoracleLocaleProjection()
    {
        SetupUser(this._sut, isAdmin: true);
        var request = new SettingsController.SiteSettingRequest { Value = "de" };

        var result = await this._sut.Upsert("poracle_locale", request);

        Assert.IsType<BadRequestObjectResult>(result);
        this._siteService.Verify(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()), Times.Never);
    }

    [Fact]
    public async Task UpsertRefusesThePoracleLocaleProjectionWhateverItsCasing()
    {
        SetupUser(this._sut, isAdmin: true);
        var request = new SettingsController.SiteSettingRequest { Value = "de" };

        Assert.IsType<BadRequestObjectResult>(await this._sut.Upsert("PORACLE_LOCALE", request));
    }

    /// <summary>
    /// The refusal must not spread: an ordinary key still writes. Without this the guard above passes
    /// just as well with the whole endpoint broken.
    /// </summary>
    [Fact]
    public async Task UpsertStillWritesAnOrdinarySetting()
    {
        SetupUser(this._sut, isAdmin: true);
        this._siteService.Setup(s => s.GetByKeyAsync("custom_title")).ReturnsAsync((SiteSetting?)null);
        this._siteService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My Site" });

        var result = await this._sut.Upsert("custom_title", new SettingsController.SiteSettingRequest { Value = "My Site" });

        Assert.IsType<OkObjectResult>(result);
        this._siteService.Verify(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()), Times.Once);
    }

    [Fact]
    public void GetDiscordConfigReturnsOkForAdmin()
    {
        var controller = this.CreateController(
            new DiscordSettings
            {
                ClientId = "123456789012345678",
                ClientSecret = "abcdefghijklmnopqrstuvwxyz123456",
                BotToken = "MTIzNDU2Nzg5.GhijKl.abcdefghijklmnop",
                GuildId = "987654321098765432",
                GeofenceForumChannelId = "111222333444555666",
            },
            new PoracleSettings
            {
                AdminIds = "111111111,222222222",
            });
        SetupUser(controller, isAdmin: true);

        var result = controller.GetDiscordConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        // Verify secrets are masked (should not contain full values)
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz123456", json);
        Assert.DoesNotContain("MTIzNDU2Nzg5.GhijKl.abcdefghijklmnop", json);
    }

    [Fact]
    public void GetDiscordConfigReturnsForbidForNonAdmin()
    {
        SetupUser(this._sut, isAdmin: false);

        var result = this._sut.GetDiscordConfig();

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpsertRejectsBothLoginMethodsDisabled()
    {
        SetupUser(this._sut, isAdmin: true);
        // enable_discord is already False in DB
        this._siteService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("False");

        var request = new SettingsController.SiteSettingRequest { Value = "False" };
        var result = await this._sut.Upsert("enable_telegram", request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.Contains("At least one login method must remain enabled", json);
    }

    [Fact]
    public async Task UpsertAllowsDisablingOneLoginMethod()
    {
        SetupUser(this._sut, isAdmin: true);
        // enable_discord is True (or absent/null) — so disabling telegram is fine
        this._siteService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("True");
        this._siteService.Setup(s => s.GetByKeyAsync("enable_telegram")).ReturnsAsync((SiteSetting?)null);
        this._siteService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        var request = new SettingsController.SiteSettingRequest { Value = "False" };
        var result = await this._sut.Upsert("enable_telegram", request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpsertAllowsDisablingWhenOtherIsAbsent()
    {
        SetupUser(this._sut, isAdmin: true);
        // enable_discord doesn't exist in DB (null = enabled by safe default)
        this._siteService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync((string?)null);
        this._siteService.Setup(s => s.GetByKeyAsync("enable_telegram")).ReturnsAsync((SiteSetting?)null);
        this._siteService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        var request = new SettingsController.SiteSettingRequest { Value = "False" };
        var result = await this._sut.Upsert("enable_telegram", request);

        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>
    /// The keys Poracle forces off, so the SPA can hide those sections and the admin page can mark
    /// the matching toggle as not-ours-to-change instead of showing a dead switch. See #769.
    /// </summary>
    [Fact]
    public async Task GetUpstreamDisabledReturnsTheKeysPoracleForcesOff()
    {
        SetupUser(this._sut, isAdmin: false);
        this._upstreamFlags
            .Setup(f => f.GetDisabledKeysAsync())
            .ReturnsAsync(new HashSet<string>(["disable_raids", "disable_quests"], StringComparer.Ordinal));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetUpstreamDisabled());
        var keys = Assert.IsType<List<string>>(ok.Value);

        Assert.Equal(["disable_quests", "disable_raids"], keys);
    }

    /// <summary>
    /// Non-admins need this to hide nav items, so it must not be admin-gated. It is also the normal
    /// case: prod serves an empty disabledHooks array.
    /// </summary>
    [Fact]
    public async Task GetUpstreamDisabledReturnsAnEmptyListForNonAdminsWhenPoracleDisablesNothing()
    {
        SetupUser(this._sut, isAdmin: false);
        this._upstreamFlags
            .Setup(f => f.GetDisabledKeysAsync())
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));

        var ok = Assert.IsType<OkObjectResult>(await this._sut.GetUpstreamDisabled());
        Assert.Empty(Assert.IsType<List<string>>(ok.Value));
    }
}
