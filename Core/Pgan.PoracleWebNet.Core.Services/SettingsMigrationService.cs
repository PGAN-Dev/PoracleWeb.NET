using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Seeds default site settings and repairs stored ones at startup. Nothing here reads
/// from Poracle's database.
/// </summary>
public partial class SettingsMigrationService(
    ISiteSettingService siteSettingService,
    IQuickPickDefinitionRepository quickPickDefinitionRepository,
    ILogger<SettingsMigrationService> logger) : ISettingsMigrationService
{
    private readonly ISiteSettingService _siteSettingService = siteSettingService;
    private readonly IQuickPickDefinitionRepository _quickPickDefinitionRepository = quickPickDefinitionRepository;
    private readonly ILogger<SettingsMigrationService> _logger = logger;

    // ── Default seeding ───────────────────────────────────────────────────

    /// <summary>
    /// Default site settings seeded on fresh installs. Each entry is only
    /// written when the key does not yet exist in the database, so admin
    /// overrides are never clobbered.
    /// </summary>
    private static readonly (string Key, string Value, string Category, string ValueType)[] DefaultSettings =
    [
        ("custom_title", "PoracleWeb.NET", "branding", "string"),
    ];

    /// <summary>Marks that the built-in quick picks have been created once. See #662, #666.</summary>
    private const string QuickPicksSeededKey = "quick_picks_seeded";

    public async Task SeedDefaultsAsync()
    {
        foreach (var (key, value, category, valueType) in DefaultSettings)
        {
            var existing = await this._siteSettingService.GetByKeyAsync(key);
            if (existing != null)
            {
                continue;
            }

            await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
            {
                Key = key,
                Value = value,
                Category = category,
                ValueType = valueType,
            });

            LogDefaultSeeded(this._logger, key, value);
        }

        await this.BackfillQuickPickSeedMarkerAsync();
        await this.RepointDeadIconPackAsync();
    }

    /// <summary>
    /// Rewrites any icon setting that points into whitewillem/PogoAssets to the pack the SPA would use
    /// for that category with nothing set.
    /// </summary>
    /// <remarks>
    /// The repository no longer exists, so every icon under it renders as nothing. #877 repointed the
    /// fallback for unset categories and left stored rows alone, which is right for a pack an operator
    /// chose and wrong for one that cannot answer: an upgraded instance holding those rows kept a site
    /// with no icons. Only the six keys the SPA reads, and only values inside the dead repository, are
    /// touched; a live or self-hosted pack is left exactly as it is. Runs on every start and writes
    /// nothing once the rows are repaired.
    /// </remarks>
    private async Task RepointDeadIconPackAsync()
    {
        foreach (var (key, folder) in IconPackDefaults.FolderByKey)
        {
            var existing = await this._siteSettingService.GetByKeyAsync(key);
            if (existing is null || !IconPackDefaults.PointsIntoDeadRepository(existing.Value))
            {
                continue;
            }

            var repointed = $"{IconPackDefaults.DefaultBase}/{folder}";
            await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
            {
                Key = existing.Key,
                Value = repointed,
                Category = existing.Category,
                ValueType = existing.ValueType,
            });

            LogIconSettingRepointed(this._logger, key, existing.Value!, repointed);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Icon setting '{Key}' pointed at a repository that no longer exists ({Old}); repointed to {New}")]
    private static partial void LogIconSettingRepointed(ILogger logger, string key, string old, string @new);

    /// <summary>
    /// Records that the built-in quick picks exist, for installations seeded before the marker did.
    /// </summary>
    /// <remarks>
    /// The marker used to be a per-browser localStorage flag and became a site setting in #662, so an
    /// installation seeded before that has no row and would seed again on the next admin visit.
    /// <para>
    /// This covers installations that still hold at least one global pick, which is the common case. It
    /// deliberately does NOT cover an admin who had already deleted every preset before upgrading: they
    /// have no global picks, so there is nothing here to infer from, and the old marker lived in their
    /// browser where the server cannot see it. That admin gets one reseed. Said plainly because the
    /// first version of this comment claimed to cover it and did not. See #666, #672.
    /// </para>
    /// </remarks>
    private async Task BackfillQuickPickSeedMarkerAsync()
    {
        if (await this._siteSettingService.GetByKeyAsync(QuickPicksSeededKey) is not null)
        {
            return;
        }

        var globals = await this._quickPickDefinitionRepository.GetAllGlobalAsync();
        if (globals is not { Count: > 0 })
        {
            // Genuinely fresh, or deliberately emptied before the marker existed. Either way the first
            // admin visit seeds, which is the behaviour that shipped.
            return;
        }

        await this._siteSettingService.CreateOrUpdateAsync(new SiteSetting
        {
            Key = QuickPicksSeededKey,
            Value = "true",
            Category = "admin",
            ValueType = "boolean",
        });

        LogDefaultSeeded(this._logger, QuickPicksSeededKey, "true");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded default setting '{Key}' = '{Value}'")]
    private static partial void LogDefaultSeeded(ILogger logger, string key, string value);
}
