using System.Text.Json;

using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// One-time data migration service that reads from the old pweb_settings KV table
/// and writes to the new structured tables (site_settings, webhook_delegates,
/// quick_pick_definitions, quick_pick_applied_states).
/// </summary>
public partial class SettingsMigrationService(
    IPwebSettingService oldSettingService,
    ISiteSettingService siteSettingService,
    IWebhookDelegateService webhookDelegateService,
    IQuickPickDefinitionRepository quickPickDefinitionRepository,
    IQuickPickAppliedStateRepository quickPickAppliedStateRepository,
    ILogger<SettingsMigrationService> logger) : ISettingsMigrationService
{
    private readonly IPwebSettingService _oldSettingService = oldSettingService;
    private readonly ISiteSettingService _siteSettingService = siteSettingService;
    private readonly IWebhookDelegateService _webhookDelegateService = webhookDelegateService;
    private readonly IQuickPickDefinitionRepository _quickPickDefinitionRepository = quickPickDefinitionRepository;
    private readonly IQuickPickAppliedStateRepository _quickPickAppliedStateRepository = quickPickAppliedStateRepository;
    private readonly ILogger<SettingsMigrationService> _logger = logger;

    private const string SentinelKey = "migration_completed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // Category mapping for site_settings
    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // branding
        ["custom_title"] = "branding",
        ["header_logo_url"] = "branding",
        ["hide_header_logo"] = "branding",
        ["custom_page_name"] = "branding",
        ["custom_page_url"] = "branding",
        ["custom_page_icon"] = "branding",

        // alarms
        ["disable_mons"] = "alarms",
        ["disable_raids"] = "alarms",
        ["disable_quests"] = "alarms",
        ["disable_invasions"] = "alarms",
        ["disable_lures"] = "alarms",
        ["disable_nests"] = "alarms",
        ["disable_gyms"] = "alarms",
        ["disable_maxbattles"] = "alarms",

        // features
        ["disable_areas"] = "features",
        ["disable_profiles"] = "features",
        ["disable_location"] = "features",
        ["disable_nominatim"] = "features",
        ["disable_geomap"] = "features",
        ["disable_geomap_select"] = "features",
        ["enable_templates"] = "features",

        // admin
        ["enable_roles"] = "admin",
        ["allowed_role_ids"] = "admin",
        ["allowed_languages"] = "admin",

        // commands
        ["register_command"] = "commands",
        ["location_command"] = "commands",

        // discord
        ["enable_discord"] = "discord",

        // telegram
        ["enable_telegram"] = "telegram",
        ["telegram_bot"] = "telegram",
        ["telegram_bot_token"] = "telegram",

        // maps
        ["provider_url"] = "maps",

        // analytics
        ["gAnalyticsId"] = "analytics",
        ["patreonUrl"] = "analytics",
        ["paypalUrl"] = "analytics",

        // debug
        ["site_is_https"] = "debug",
        ["debug"] = "debug",

        // icons
        ["uicons_pkmn"] = "icons",
        ["uicons_gym"] = "icons",
        ["uicons_raid"] = "icons",
        ["uicons_reward"] = "icons",
        ["uicons_item"] = "icons",
        ["uicons_type"] = "icons",

        // api
        ["api_address"] = "api",
        ["api_secret"] = "api",
    };

    // Keys whose values are booleans
    private static readonly HashSet<string> BooleanKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "disable_mons", "disable_raids", "disable_quests", "disable_invasions",
        "disable_lures", "disable_nests", "disable_gyms", "disable_maxbattles", "disable_areas",
        "disable_profiles", "disable_location", "disable_nominatim",
        "disable_geomap", "disable_geomap_select",
        "enable_templates", "enable_roles", "enable_telegram", "enable_discord",
        "hide_header_logo", "site_is_https", "debug",
    };

    // Keys whose values are URLs
    private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "header_logo_url", "custom_page_url", "provider_url",
        "patreonUrl", "paypalUrl",
        "uicons_pkmn", "uicons_gym", "uicons_raid",
        "uicons_reward", "uicons_item", "uicons_type",
    };

    // Keys whose values are comma-separated lists
    private static readonly HashSet<string> CsvKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "allowed_role_ids", "allowed_languages",
    };

    public async Task MigrateAsync()
    {
        // 1. Check sentinel — idempotent guard
        var sentinel = await this._siteSettingService.GetByKeyAsync(SentinelKey);
        if (sentinel != null)
        {
            LogMigrationAlreadyCompleted(this._logger);
            return;
        }

        LogMigrationStarting(this._logger);

        // 2. Load all old settings
        var oldSettings = (await this._oldSettingService.GetAllAsync()).ToList();
        LogOldSettingsLoaded(this._logger, oldSettings.Count);

        var counts = new Dictionary<string, int>
        {
            ["site_settings"] = 0,
            ["webhook_delegates"] = 0,
            ["quick_pick_definitions"] = 0,
            ["quick_pick_applied_states"] = 0,
            ["skipped"] = 0,
            ["failed"] = 0,
        };

        // 3. Categorize and migrate each setting
        foreach (var setting in oldSettings)
        {
            try
            {
                if (setting.Setting.StartsWith("webhook_delegates:", StringComparison.Ordinal))
                {
                    await this.MigrateWebhookDelegateAsync(setting, counts);
                }
                else if (setting.Setting.StartsWith("user_quick_pick:", StringComparison.Ordinal))
                {
                    await this.MigrateUserQuickPickAsync(setting, counts);
                }
                else if (setting.Setting.StartsWith("quick_pick:", StringComparison.Ordinal))
                {
                    await this.MigrateAdminQuickPickAsync(setting, counts);
                }
                else if (setting.Setting.StartsWith("qp_applied:", StringComparison.Ordinal))
                {
                    await this.MigrateQuickPickAppliedStateAsync(setting, counts);
                }
                else
                {
                    await this.MigrateSiteSettingAsync(setting, counts);
                }
            }
            catch (Exception ex)
            {
                counts["failed"]++;
                LogSettingMigrationFailed(this._logger, ex, setting.Setting);
            }
        }

        // 4. Write sentinel
        await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
        {
            Key = SentinelKey,
            Value = "true",
            Category = "system",
            ValueType = "boolean",
        });

        // 5. Log results
        LogMigrationCompleted(
            this._logger,
            counts["site_settings"],
            counts["webhook_delegates"],
            counts["quick_pick_definitions"],
            counts["quick_pick_applied_states"],
            counts["failed"]);
    }

    private async Task MigrateWebhookDelegateAsync(PwebSetting setting, Dictionary<string, int> counts)
    {
        // Key format: "webhook_delegates:{webhookId}"
        var webhookId = setting.Setting["webhook_delegates:".Length..];
        if (string.IsNullOrEmpty(webhookId) || string.IsNullOrEmpty(setting.Value))
        {
            counts["skipped"]++;
            return;
        }

        var userIds = setting.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var userId in userIds)
        {
            try
            {
                await this._webhookDelegateService.AddDelegateAsync(webhookId, userId);
                counts["webhook_delegates"]++;
            }
            catch (Exception ex)
            {
                LogDelegateMigrationFailed(this._logger, ex, webhookId, userId);
                counts["failed"]++;
            }
        }
    }

    private async Task MigrateAdminQuickPickAsync(PwebSetting setting, Dictionary<string, int> counts)
    {
        // Key format: "quick_pick:{id}"
        if (string.IsNullOrEmpty(setting.Value))
        {
            counts["skipped"]++;
            return;
        }

        var definition = JsonSerializer.Deserialize<QuickPickDefinition>(setting.Value, JsonOptions);
        if (definition == null)
        {
            counts["skipped"]++;
            return;
        }

        definition.Scope = "global";
        await this._quickPickDefinitionRepository.CreateOrUpdateAsync(definition);
        counts["quick_pick_definitions"]++;
    }

    private async Task MigrateUserQuickPickAsync(PwebSetting setting, Dictionary<string, int> counts)
    {
        // Key format: "user_quick_pick:{userId}:{id}"
        if (string.IsNullOrEmpty(setting.Value))
        {
            counts["skipped"]++;
            return;
        }

        // Extract userId from the key
        var afterPrefix = setting.Setting["user_quick_pick:".Length..];
        var colonIdx = afterPrefix.IndexOf(':');
        if (colonIdx < 0)
        {
            counts["skipped"]++;
            return;
        }

        var userId = afterPrefix[..colonIdx];

        var definition = JsonSerializer.Deserialize<QuickPickDefinition>(setting.Value, JsonOptions);
        if (definition == null)
        {
            counts["skipped"]++;
            return;
        }

        definition.Scope = "user";
        definition.OwnerUserId = userId;
        await this._quickPickDefinitionRepository.CreateOrUpdateAsync(definition);
        counts["quick_pick_definitions"]++;
    }

    private async Task MigrateQuickPickAppliedStateAsync(PwebSetting setting, Dictionary<string, int> counts)
    {
        // Key format: "qp_applied:{userId}:{profileNo}:{quickPickId}"
        if (string.IsNullOrEmpty(setting.Value))
        {
            counts["skipped"]++;
            return;
        }

        var afterPrefix = setting.Setting["qp_applied:".Length..];
        var parts = afterPrefix.Split(':', 3);
        if (parts.Length < 3)
        {
            LogInvalidAppliedStateKey(this._logger, setting.Setting);
            counts["skipped"]++;
            return;
        }

        var userId = parts[0];
        if (!int.TryParse(parts[1], out var profileNo))
        {
            LogInvalidAppliedStateKey(this._logger, setting.Setting);
            counts["skipped"]++;
            return;
        }

        var quickPickId = parts[2];

        var state = JsonSerializer.Deserialize<QuickPickAppliedState>(setting.Value, JsonOptions);
        if (state == null)
        {
            counts["skipped"]++;
            return;
        }

        state.QuickPickId = quickPickId;
        state.UserId = userId;
        state.ProfileNo = profileNo;
        await this._quickPickAppliedStateRepository.CreateOrUpdateAsync(state);
        counts["quick_pick_applied_states"]++;
    }

    private async Task MigrateSiteSettingAsync(PwebSetting setting, Dictionary<string, int> counts)
    {
        var category = CategoryMap.GetValueOrDefault(setting.Setting, "other");
        var valueType = DetermineValueType(setting.Setting);

        await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
        {
            Key = setting.Setting,
            Value = setting.Value,
            Category = category,
            ValueType = valueType,
        });

        counts["site_settings"]++;
    }

    private static string DetermineValueType(string key)
    {
        if (BooleanKeys.Contains(key))
        {
            return "boolean";
        }

        if (UrlKeys.Contains(key))
        {
            return "url";
        }

        if (CsvKeys.Contains(key))
        {
            return "csv";
        }

        return "string";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings migration already completed, skipping")]
    private static partial void LogMigrationAlreadyCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting settings migration from pweb_settings to structured tables")]
    private static partial void LogMigrationStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} old pweb_settings records")]
    private static partial void LogOldSettingsLoaded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to migrate setting '{Key}'")]
    private static partial void LogSettingMigrationFailed(ILogger logger, Exception ex, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to migrate webhook delegate for webhook '{WebhookId}', user '{UserId}'")]
    private static partial void LogDelegateMigrationFailed(ILogger logger, Exception ex, string webhookId, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid qp_applied key format: '{Key}'")]
    private static partial void LogInvalidAppliedStateKey(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings migration completed: {SiteSettings} site settings, {WebhookDelegates} webhook delegates, {QuickPickDefinitions} quick pick definitions, {QuickPickAppliedStates} quick pick applied states, {Failed} failed")]
    private static partial void LogMigrationCompleted(ILogger logger, int siteSettings, int webhookDelegates, int quickPickDefinitions, int quickPickAppliedStates, int failed);
}
