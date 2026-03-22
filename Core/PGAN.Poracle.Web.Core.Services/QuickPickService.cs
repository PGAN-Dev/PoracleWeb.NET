using System.Text.Json;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public partial class QuickPickService(
    IPwebSettingService settingService,
    IMonsterService monsterService,
    IRaidService raidService,
    IEggService eggService,
    IQuestService questService,
    IInvasionService invasionService,
    ILureService lureService,
    INestService nestService,
    IGymService gymService,
    IMasterDataService masterDataService,
    ILogger<QuickPickService> logger) : IQuickPickService
{
    private readonly IPwebSettingService _settingService = settingService;
    private readonly IMonsterService _monsterService = monsterService;
    private readonly IRaidService _raidService = raidService;
    private readonly IEggService _eggService = eggService;
    private readonly IQuestService _questService = questService;
    private readonly IInvasionService _invasionService = invasionService;
    private readonly ILureService _lureService = lureService;
    private readonly INestService _nestService = nestService;
    private readonly IGymService _gymService = gymService;
    private readonly IMasterDataService _masterDataService = masterDataService;
    private readonly ILogger<QuickPickService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private const string AdminKeyPrefix = "quick_pick:";
    private const string UserKeyPrefix = "user_quick_pick:";
    private const string AppliedKeyPrefix = "qp_applied:";

    public async Task<IEnumerable<QuickPickSummary>> GetAllAsync(string userId, int profileNo)
    {
        var allSettings = await this._settingService.GetAllAsync();
        var settingsList = allSettings.ToList();

        var userPrefix = $"{UserKeyPrefix}{userId}:";
        var summaries = new List<QuickPickSummary>();

        foreach (var setting in settingsList)
        {
            QuickPickDefinition? definition = null;

            if (setting.Setting.StartsWith(AdminKeyPrefix, StringComparison.Ordinal))
            {
                definition = DeserializeDefinition(setting.Value);
            }
            else if (setting.Setting.StartsWith(userPrefix, StringComparison.Ordinal))
            {
                definition = DeserializeDefinition(setting.Value);
            }

            if (definition == null || !definition.Enabled)
            {
                continue;
            }

            var appliedKey = $"{AppliedKeyPrefix}{userId}:{profileNo}:{definition.Id}";
            var appliedSetting = settingsList.FirstOrDefault(s => s.Setting == appliedKey);
            QuickPickAppliedState? appliedState = null;

            if (appliedSetting?.Value != null)
            {
                appliedState = JsonSerializer.Deserialize<QuickPickAppliedState>(appliedSetting.Value, JsonOptions);

                // Verify tracked alarms still exist — if all deleted manually, clear applied state
                if (appliedState?.TrackedUids is { Count: > 0 })
                {
                    var remaining = await this.CountRemainingUidsAsync(definition.AlarmType, appliedState.TrackedUids);
                    if (remaining == 0)
                    {
                        // All alarms were deleted manually — clean up stale applied state
                        await this._settingService.DeleteAsync(appliedKey);
                        appliedState = null;
                    }
                    else if (remaining < appliedState.TrackedUids.Count)
                    {
                        // Some alarms were deleted — update the tracked UIDs to only valid ones
                        appliedState.TrackedUids = await this.GetValidUidsAsync(definition.AlarmType, appliedState.TrackedUids);
                        var updatedJson = JsonSerializer.Serialize(appliedState, JsonOptions);
                        await this._settingService.CreateOrUpdateAsync(new PwebSetting { Setting = appliedKey, Value = updatedJson });
                    }
                }
            }

            summaries.Add(new QuickPickSummary
            {
                Definition = definition,
                AppliedState = appliedState
            });
        }

        return summaries.OrderBy(s => s.Definition.SortOrder).ThenBy(s => s.Definition.Name);
    }

    public async Task<QuickPickDefinition?> GetByIdAsync(string id)
    {
        // Check admin picks first, then all user picks
        var setting = await this._settingService.GetByKeyAsync($"{AdminKeyPrefix}{id}");
        if (setting?.Value != null)
        {
            return DeserializeDefinition(setting.Value);
        }

        // For user picks we'd need the userId, but this method is used for admin lookup
        return null;
    }

    public async Task<QuickPickDefinition> SaveAdminPickAsync(QuickPickDefinition definition)
    {
        definition.Scope = "global";
        var key = $"{AdminKeyPrefix}{definition.Id}";
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        await this._settingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = json });

        return definition;
    }

    public async Task<QuickPickDefinition> SaveUserPickAsync(string userId, QuickPickDefinition definition)
    {
        definition.Scope = "user";
        var key = $"{UserKeyPrefix}{userId}:{definition.Id}";
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        await this._settingService.CreateOrUpdateAsync(new PwebSetting { Setting = key, Value = json });

        return definition;
    }

    public async Task<bool> DeleteAdminPickAsync(string id) => await this._settingService.DeleteAsync($"{AdminKeyPrefix}{id}");

    public async Task<bool> DeleteUserPickAsync(string userId, string id) => await this._settingService.DeleteAsync($"{UserKeyPrefix}{userId}:{id}");

    public async Task<QuickPickAppliedState> ApplyAsync(
        string userId, int profileNo, string quickPickId, QuickPickApplyRequest request)
    {
        var definition = await this.LoadDefinitionAsync(userId, quickPickId) ?? throw new InvalidOperationException($"Quick pick '{quickPickId}' not found.");

        var trackedUids = new List<int>();

        trackedUids = definition.AlarmType switch
        {
            "monster" => await this.ApplyMonsterAsync(userId, profileNo, definition, request),
            "raid" => await this.ApplyRaidAsync(userId, profileNo, definition, request),
            "egg" => await this.ApplyEggAsync(userId, profileNo, definition, request),
            "quest" => await this.ApplyQuestAsync(userId, profileNo, definition, request),
            "invasion" => await this.ApplyInvasionAsync(userId, profileNo, definition, request),
            "lure" => await this.ApplyLureAsync(userId, profileNo, definition, request),
            "nest" => await this.ApplyNestAsync(userId, profileNo, definition, request),
            "gym" => await this.ApplyGymAsync(userId, profileNo, definition, request),
            _ => throw new InvalidOperationException($"Unknown alarm type '{definition.AlarmType}'."),
        };
        var appliedState = new QuickPickAppliedState
        {
            QuickPickId = quickPickId,
            AppliedAt = DateTime.UtcNow,
            ExcludePokemonIds = request.ExcludePokemonIds,
            TrackedUids = trackedUids
        };

        var appliedKey = $"{AppliedKeyPrefix}{userId}:{profileNo}:{quickPickId}";
        var json = JsonSerializer.Serialize(appliedState, JsonOptions);
        await this._settingService.CreateOrUpdateAsync(new PwebSetting { Setting = appliedKey, Value = json });

        LogQuickPickApplied(this._logger, quickPickId, userId, profileNo, trackedUids.Count);

        return appliedState;
    }

    public async Task<QuickPickAppliedState> ReapplyAsync(
        string userId, int profileNo, string quickPickId, QuickPickApplyRequest request)
    {
        await this.RemoveAsync(userId, profileNo, quickPickId);
        return await this.ApplyAsync(userId, profileNo, quickPickId, request);
    }

    public async Task<bool> RemoveAsync(string userId, int profileNo, string quickPickId)
    {
        var appliedKey = $"{AppliedKeyPrefix}{userId}:{profileNo}:{quickPickId}";
        var setting = await this._settingService.GetByKeyAsync(appliedKey);

        if (setting?.Value == null)
        {
            return false;
        }

        var appliedState = JsonSerializer.Deserialize<QuickPickAppliedState>(setting.Value, JsonOptions);
        if (appliedState == null)
        {
            return false;
        }

        // Load the definition to determine alarm type for deletion
        var definition = await this.LoadDefinitionAsync(userId, quickPickId);
        var alarmType = definition?.AlarmType ?? "monster";

        // Delete each tracked alarm row
        foreach (var uid in appliedState.TrackedUids)
        {
            switch (alarmType)
            {
                case "monster":
                    await this._monsterService.DeleteAsync(uid);
                    break;
                case "raid":
                    await this._raidService.DeleteAsync(uid);
                    break;
                case "egg":
                    await this._eggService.DeleteAsync(uid);
                    break;
                case "quest":
                    await this._questService.DeleteAsync(uid);
                    break;
                case "invasion":
                    await this._invasionService.DeleteAsync(uid);
                    break;
                case "lure":
                    await this._lureService.DeleteAsync(uid);
                    break;
                case "nest":
                    await this._nestService.DeleteAsync(uid);
                    break;
                case "gym":
                    await this._gymService.DeleteAsync(uid);
                    break;
                default:
                    break;
            }
        }

        // Delete the applied state
        await this._settingService.DeleteAsync(appliedKey);

        LogQuickPickRemoved(this._logger, quickPickId, userId, profileNo, appliedState.TrackedUids.Count);

        return true;
    }

    public Task<IEnumerable<QuickPickDefinition>> GetDefaultPicksAsync() => Task.FromResult<IEnumerable<QuickPickDefinition>>(Defaults);

    public async Task SeedDefaultsAsync()
    {
        // Delete any existing admin quick picks so we can re-seed cleanly
        var allSettings = await this._settingService.GetAllAsync();
        var existingKeys = allSettings
            .Where(s => s.Setting.StartsWith(AdminKeyPrefix, StringComparison.Ordinal))
            .Select(s => s.Setting)
            .ToList();

        foreach (var key in existingKeys)
        {
            await this._settingService.DeleteAsync(key);
        }

        LogSeedingDefaults(this._logger, Defaults.Count, existingKeys.Count);

        foreach (var definition in Defaults)
        {
            await this.SaveAdminPickAsync(definition);
        }
    }

    // --- Private helpers ---

    private async Task<QuickPickDefinition?> LoadDefinitionAsync(string userId, string quickPickId)
    {
        // Check admin picks first
        var setting = await this._settingService.GetByKeyAsync($"{AdminKeyPrefix}{quickPickId}");
        if (setting?.Value != null)
        {
            return DeserializeDefinition(setting.Value);
        }

        // Check user picks
        setting = await this._settingService.GetByKeyAsync($"{UserKeyPrefix}{userId}:{quickPickId}");
        if (setting?.Value != null)
        {
            return DeserializeDefinition(setting.Value);
        }

        return null;
    }

    private static QuickPickDefinition? DeserializeDefinition(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QuickPickDefinition>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // --- Pokemon ID expansion ---

    private async Task<List<int>> GetAllPokemonIds()
    {
        var pokemonJson = await this._masterDataService.GetPokemonDataAsync();
        if (string.IsNullOrEmpty(pokemonJson))
        {
            return [];
        }

        var pokemonMap = JsonSerializer.Deserialize<Dictionary<string, string>>(pokemonJson);
        if (pokemonMap == null)
        {
            return [];
        }

        return [.. pokemonMap.Keys
            .Select(k => int.TryParse(k, out var id) ? id : 0)
            .Where(id => id > 0)
            .OrderBy(id => id)];
    }

    // --- Monster ---

    private async Task<List<int>> ApplyMonsterAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var pokemonId = GetFilterInt(definition.Filters, "pokemonId");

        if (pokemonId == 0 && request.ExcludePokemonIds.Count > 0)
        {
            // Exclusions specified — expand to individual rows, minus excluded
            var allIds = await this.GetAllPokemonIds();
            var excludeSet = new HashSet<int>(request.ExcludePokemonIds);
            var filteredIds = allIds.Where(id => !excludeSet.Contains(id)).ToList();

            var monsters = filteredIds.Select(id => BuildMonster(definition.Filters, id, profileNo, request)).ToList();
            var created = await this._monsterService.BulkCreateAsync(userId, monsters);
            return [.. created.Select(m => m.Uid)];
        }
        else
        {
            // No exclusions or specific Pokemon — single row (pokemon_id=0 for "all")
            var monster = BuildMonster(definition.Filters, pokemonId, profileNo, request);
            var created = await this._monsterService.CreateAsync(userId, monster);
            return [created.Uid];
        }
    }

    private static Monster BuildMonster(Dictionary<string, object?> filters, int pokemonId, int profileNo, QuickPickApplyRequest request)
    {
        // Start with sensible defaults (matching the add dialog defaults)
        var monster = new Monster
        {
            MaxIv = 100,
            MaxCp = 9000,
            MaxLevel = 40,
            MaxWeight = 9000000,
            MaxAtk = 15,
            MaxDef = 15,
            MaxSta = 15,
            PvpRankingBest = 1,
            PvpRankingWorst = 100,
        };

        // Overlay the quick pick filters on top of the defaults
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var overrides = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                var prop = typeof(Monster).GetProperty(key, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(int) && value.TryGetInt32(out var intVal))
                    {
                        prop.SetValue(monster, intVal);
                    }
                    else if (prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(monster, value.GetString());
                    }
                }
            }
        }

        monster.PokemonId = pokemonId;
        monster.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            monster.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            monster.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            monster.Template = request.Template;
        }

        return monster;
    }

    // --- Raid ---

    private async Task<List<int>> ApplyRaidAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        // Raids with pokemonId=0 are level-based (Poracle handles matching) - create single row
        var raid = BuildRaid(definition.Filters, profileNo, request);
        var created = await this._raidService.CreateAsync(userId, raid);
        return [created.Uid];
    }

    private static Raid BuildRaid(Dictionary<string, object?> filters, int profileNo, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var raid = JsonSerializer.Deserialize<Raid>(json, JsonOptions) ?? new Raid();

        raid.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            raid.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            raid.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            raid.Template = request.Template;
        }

        return raid;
    }

    // --- Egg ---

    private async Task<List<int>> ApplyEggAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var egg = JsonSerializer.Deserialize<Egg>(json, JsonOptions) ?? new Egg();

        egg.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            egg.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            egg.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            egg.Template = request.Template;
        }

        var created = await this._eggService.CreateAsync(userId, egg);
        return [created.Uid];
    }

    // --- Quest ---

    private async Task<List<int>> ApplyQuestAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var quest = JsonSerializer.Deserialize<Quest>(json, JsonOptions) ?? new Quest();

        quest.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            quest.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            quest.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            quest.Template = request.Template;
        }

        var created = await this._questService.CreateAsync(userId, quest);
        return [created.Uid];
    }

    // --- Invasion ---

    private async Task<List<int>> ApplyInvasionAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var invasion = JsonSerializer.Deserialize<Invasion>(json, JsonOptions) ?? new Invasion();

        invasion.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            invasion.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            invasion.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            invasion.Template = request.Template;
        }

        var created = await this._invasionService.CreateAsync(userId, invasion);
        return [created.Uid];
    }

    // --- Lure ---

    private async Task<List<int>> ApplyLureAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var lure = JsonSerializer.Deserialize<Lure>(json, JsonOptions) ?? new Lure();

        lure.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            lure.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            lure.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            lure.Template = request.Template;
        }

        var created = await this._lureService.CreateAsync(userId, lure);
        return [created.Uid];
    }

    // --- Nest ---

    private async Task<List<int>> ApplyNestAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var nest = JsonSerializer.Deserialize<Nest>(json, JsonOptions) ?? new Nest();

        nest.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            nest.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            nest.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            nest.Template = request.Template;
        }

        var created = await this._nestService.CreateAsync(userId, nest);
        return [created.Uid];
    }

    // --- Gym ---

    private async Task<List<int>> ApplyGymAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var gym = JsonSerializer.Deserialize<Gym>(json, JsonOptions) ?? new Gym();

        gym.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            gym.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            gym.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            gym.Template = request.Template;
        }

        var created = await this._gymService.CreateAsync(userId, gym);
        return [created.Uid];
    }

    // --- Utility ---

    private static int GetFilterInt(Dictionary<string, object?> filters, string key)
    {
        if (!filters.TryGetValue(key, out var value) || value == null)
        {
            return 0;
        }

        if (value is JsonElement element)
        {
            return element.TryGetInt32(out var intVal) ? intVal : 0;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return (int)l;
        }

        if (value is double d)
        {
            return (int)d;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    // --- UID validation helpers ---

    private async Task<int> CountRemainingUidsAsync(string alarmType, List<int> uids)
    {
        var count = 0;
        foreach (var uid in uids)
        {
            var exists = alarmType switch
            {
                "monster" => await this._monsterService.GetByUidAsync(uid) != null,
                "raid" => await this._raidService.GetByUidAsync(uid) != null,
                "egg" => await this._eggService.GetByUidAsync(uid) != null,
                "quest" => await this._questService.GetByUidAsync(uid) != null,
                "invasion" => await this._invasionService.GetByUidAsync(uid) != null,
                "lure" => await this._lureService.GetByUidAsync(uid) != null,
                "nest" => await this._nestService.GetByUidAsync(uid) != null,
                "gym" => await this._gymService.GetByUidAsync(uid) != null,
                _ => false,
            };
            if (exists)
            {
                count++;
            }
        }

        return count;
    }

    private async Task<List<int>> GetValidUidsAsync(string alarmType, List<int> uids)
    {
        var valid = new List<int>();
        foreach (var uid in uids)
        {
            var exists = alarmType switch
            {
                "monster" => await this._monsterService.GetByUidAsync(uid) != null,
                "raid" => await this._raidService.GetByUidAsync(uid) != null,
                "egg" => await this._eggService.GetByUidAsync(uid) != null,
                "quest" => await this._questService.GetByUidAsync(uid) != null,
                "invasion" => await this._invasionService.GetByUidAsync(uid) != null,
                "lure" => await this._lureService.GetByUidAsync(uid) != null,
                "nest" => await this._nestService.GetByUidAsync(uid) != null,
                "gym" => await this._gymService.GetByUidAsync(uid) != null,
                _ => false,
            };
            if (exists)
            {
                valid.Add(uid);
            }
        }

        return valid;
    }

    // --- Default Quick Picks ---

    private static readonly List<QuickPickDefinition> Defaults =
    [
        // ── Common (from PoracleWeb) ──
        new() { Id = "hundo", Name = "100% IV Pokemon", Description = "Track all perfect IV wild spawns", Icon = "star", Category = "Common", AlarmType = "monster", SortOrder = 1, Filters = new() { ["minIv"] = 100, ["maxIv"] = 100 } },
        new() { Id = "nundo", Name = "0% IV Pokemon", Description = "Track all zero IV wild spawns", Icon = "exposure_zero", Category = "Common", AlarmType = "monster", SortOrder = 2, Filters = new() { ["minIv"] = 0, ["maxIv"] = 0 } },
        new() { Id = "high-iv", Name = "90%+ IV Pokemon", Description = "Track all Pokemon with 90% IV or higher", Icon = "star_half", Category = "Common", AlarmType = "monster", SortOrder = 3, Filters = new() { ["minIv"] = 90 } },
        new() { Id = "high-level", Name = "Level 30+ Pokemon", Description = "Track all high-level wild spawns (weather boosted)", Icon = "trending_up", Category = "Common", AlarmType = "monster", SortOrder = 4, Filters = new() { ["minLevel"] = 30 } },
        new() { Id = "high-cp", Name = "3000+ CP Pokemon", Description = "Track strong wild spawns for gym defense", Icon = "fitness_center", Category = "Common", AlarmType = "monster", SortOrder = 5, Filters = new() { ["minCp"] = 3000 } },

        // ── PvP (from PoracleWeb + expanded) ──
        new() { Id = "pvp-great-1", Name = "PvP Great Rank 1", Description = "Track rank 1 Pokemon for Great League (1500 CP)", Icon = "emoji_events", Category = "PvP", AlarmType = "monster", SortOrder = 10, Filters = new() { ["pvpRankingLeague"] = 1500, ["pvpRankingWorst"] = 1, ["pvpRankingBest"] = 1 } },
        new() { Id = "pvp-great-10", Name = "PvP Great Top 10", Description = "Track top 10 ranked Pokemon for Great League", Icon = "emoji_events", Category = "PvP", AlarmType = "monster", SortOrder = 11, Filters = new() { ["pvpRankingLeague"] = 1500, ["pvpRankingWorst"] = 10, ["pvpRankingBest"] = 1 } },
        new() { Id = "pvp-ultra-1", Name = "PvP Ultra Rank 1", Description = "Track rank 1 Pokemon for Ultra League (2500 CP)", Icon = "emoji_events", Category = "PvP", AlarmType = "monster", SortOrder = 12, Filters = new() { ["pvpRankingLeague"] = 2500, ["pvpRankingWorst"] = 1, ["pvpRankingBest"] = 1 } },
        new() { Id = "pvp-ultra-10", Name = "PvP Ultra Top 10", Description = "Track top 10 ranked Pokemon for Ultra League", Icon = "emoji_events", Category = "PvP", AlarmType = "monster", SortOrder = 13, Filters = new() { ["pvpRankingLeague"] = 2500, ["pvpRankingWorst"] = 10, ["pvpRankingBest"] = 1 } },
        new() { Id = "pvp-little-1", Name = "PvP Little Rank 1", Description = "Track rank 1 Pokemon for Little Cup (500 CP)", Icon = "emoji_events", Category = "PvP", AlarmType = "monster", SortOrder = 14, Filters = new() { ["pvpRankingLeague"] = 500, ["pvpRankingWorst"] = 1, ["pvpRankingBest"] = 1 } },

        // ── Size (from PoracleWeb) ──
        new() { Id = "xxl", Name = "XXL Pokemon", Description = "Track all jumbo sized Pokemon for the XXL medal", Icon = "open_in_full", Category = "Size", AlarmType = "monster", SortOrder = 20, Filters = new() { ["size"] = 5 } },
        new() { Id = "xxs", Name = "XXS Pokemon", Description = "Track all tiny Pokemon for the XXS medal", Icon = "close_fullscreen", Category = "Size", AlarmType = "monster", SortOrder = 21, Filters = new() { ["size"] = 1, ["maxSize"] = 1 } },

        // ── Raids ──
        new() { Id = "raid-mega", Name = "All Mega Raids", Description = "Track all Mega and Primal raid bosses", Icon = "shield", Category = "Raids", AlarmType = "raid", SortOrder = 30, Filters = new() { ["level"] = 6 } },
        new() { Id = "raid-5star", Name = "All 5-Star Raids", Description = "Track all legendary and mythical raid bosses", Icon = "shield", Category = "Raids", AlarmType = "raid", SortOrder = 31, Filters = new() { ["level"] = 5 } },
        new() { Id = "raid-shadow", Name = "All Shadow Raids", Description = "Track all shadow raid bosses", Icon = "shield", Category = "Raids", AlarmType = "raid", SortOrder = 32, Filters = new() { ["level"] = 4 } },
        new() { Id = "raid-3star", Name = "All 3-Star Raids", Description = "Track all 3-star raid bosses", Icon = "shield", Category = "Raids", AlarmType = "raid", SortOrder = 33, Filters = new() { ["level"] = 3 } },
        new() { Id = "raid-1star", Name = "All 1-Star Raids", Description = "Track all 1-star raid bosses", Icon = "shield", Category = "Raids", AlarmType = "raid", SortOrder = 34, Filters = new() { ["level"] = 1 } },
        new() { Id = "raid-ex", Name = "EX Eligible Raids", Description = "Track raids at EX-eligible gyms", Icon = "star_border", Category = "Raids", AlarmType = "raid", SortOrder = 35, Filters = new() { ["exclusive"] = 1 } },

        // ── Eggs ──
        new() { Id = "egg-5star", Name = "5-Star Eggs", Description = "Track legendary raid eggs", Icon = "egg", Category = "Raids", AlarmType = "egg", SortOrder = 36, Filters = new() { ["level"] = 5 } },
        new() { Id = "egg-mega", Name = "Mega Eggs", Description = "Track Mega raid eggs", Icon = "egg", Category = "Raids", AlarmType = "egg", SortOrder = 37, Filters = new() { ["level"] = 6 } },

        // ── Quests ──
        new() { Id = "quest-stardust", Name = "Stardust Quests", Description = "Track field research rewarding stardust", Icon = "assignment", Category = "Quests", AlarmType = "quest", SortOrder = 40, Filters = new() { ["rewardType"] = 3 } },
        new() { Id = "quest-pokemon", Name = "Pokemon Encounter Quests", Description = "Track field research rewarding Pokemon encounters", Icon = "catching_pokemon", Category = "Quests", AlarmType = "quest", SortOrder = 41, Filters = new() { ["rewardType"] = 7 } },
        new() { Id = "quest-rare-candy", Name = "Rare Candy Quests", Description = "Track field research rewarding rare candy", Icon = "assignment", Category = "Quests", AlarmType = "quest", SortOrder = 42, Filters = new() { ["rewardType"] = 2, ["reward"] = 1301 } },

        // ── Invasions ──
        new() { Id = "all-invasions", Name = "All Invasions", Description = "Track all Team Rocket grunt and leader invasions", Icon = "warning", Category = "Invasions", AlarmType = "invasion", SortOrder = 50, Filters = [] },
        new() { Id = "invasion-leader", Name = "Rocket Leaders", Description = "Track Sierra, Cliff, and Arlo encounters", Icon = "supervisor_account", Category = "Invasions", AlarmType = "invasion", SortOrder = 51, Filters = new() { ["gruntType"] = "mixed" } },

        // ── Lures ──
        new() { Id = "lure-glacial", Name = "Glacial Lures", Description = "Track Glacial Lure Modules at PokeStops", Icon = "ac_unit", Category = "Lures", AlarmType = "lure", SortOrder = 60, Filters = new() { ["lureId"] = 502 } },
        new() { Id = "lure-magnetic", Name = "Magnetic Lures", Description = "Track Magnetic Lure Modules at PokeStops", Icon = "bolt", Category = "Lures", AlarmType = "lure", SortOrder = 61, Filters = new() { ["lureId"] = 501 } },
        new() { Id = "lure-mossy", Name = "Mossy Lures", Description = "Track Mossy Lure Modules at PokeStops", Icon = "eco", Category = "Lures", AlarmType = "lure", SortOrder = 62, Filters = new() { ["lureId"] = 503 } },
        new() { Id = "lure-rainy", Name = "Rainy Lures", Description = "Track Rainy Lure Modules at PokeStops", Icon = "water_drop", Category = "Lures", AlarmType = "lure", SortOrder = 63, Filters = new() { ["lureId"] = 504 } },
        new() { Id = "lure-golden", Name = "Golden Lures", Description = "Track Golden Lure Modules at PokeStops", Icon = "stars", Category = "Lures", AlarmType = "lure", SortOrder = 64, Filters = new() { ["lureId"] = 505 } },
    ];

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied quick pick '{QuickPickId}' for user {UserId} profile {ProfileNo}, created {Count} alarm(s).")]
    private static partial void LogQuickPickApplied(ILogger logger, string quickPickId, string userId, int profileNo, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed quick pick '{QuickPickId}' for user {UserId} profile {ProfileNo}, deleted {Count} alarm(s).")]
    private static partial void LogQuickPickRemoved(ILogger logger, string quickPickId, string userId, int profileNo, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding {Count} default quick picks (replaced {Existing} existing).")]
    private static partial void LogSeedingDefaults(ILogger logger, int count, int existing);
}
