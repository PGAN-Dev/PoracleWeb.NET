using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class QuickPickService(
    IQuickPickDefinitionRepository definitionRepository,
    IQuickPickAppliedStateRepository appliedStateRepository,
    IMonsterService monsterService,
    IRaidService raidService,
    IEggService eggService,
    IQuestService questService,
    IInvasionService invasionService,
    ILureService lureService,
    INestService nestService,
    IGymService gymService,
    IMaxBattleService maxBattleService,
    IMasterDataService masterDataService,
    IFeatureGate featureGate,
    ILogger<QuickPickService> logger) : IQuickPickService
{
    private readonly IQuickPickDefinitionRepository _definitionRepository = definitionRepository;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IQuickPickAppliedStateRepository _appliedStateRepository = appliedStateRepository;
    private readonly IMonsterService _monsterService = monsterService;
    private readonly IRaidService _raidService = raidService;
    private readonly IEggService _eggService = eggService;
    private readonly IQuestService _questService = questService;
    private readonly IInvasionService _invasionService = invasionService;
    private readonly ILureService _lureService = lureService;
    private readonly INestService _nestService = nestService;
    private readonly IGymService _gymService = gymService;
    private readonly IMaxBattleService _maxBattleService = maxBattleService;
    private readonly IMasterDataService _masterDataService = masterDataService;
    private readonly ILogger<QuickPickService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // Whitelist of Monster filter properties safe to set via reflection in BuildMonster.
    // Excludes Uid, Id, PokemonId, ProfileNo which are set explicitly.
    private static readonly HashSet<string> SafeMonsterFilterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "minIv", "maxIv", "minCp", "maxCp", "minLevel", "maxLevel",
        "minWeight", "maxWeight", "atk", "def", "sta", "maxAtk", "maxDef", "maxSta",
        "pvpRankingWorst", "pvpRankingBest", "pvpRankingMinCp", "pvpRankingLeague", "pvpRankingCap",
        "size", "maxSize",
        "form", "gender", "clean", "template", "distance", "ping",
    };

    public async Task<IEnumerable<QuickPickSummary>> GetAllAsync(string userId, int profileNo)
    {
        var globalPicks = await this._definitionRepository.GetAllGlobalAsync();
        var userPicks = await this._definitionRepository.GetByOwnerAsync(userId);

        var allDefinitions = new List<QuickPickDefinition>(globalPicks.Count + userPicks.Count);
        allDefinitions.AddRange(globalPicks);
        allDefinitions.AddRange(userPicks);

        var summaries = new List<QuickPickSummary>();

        foreach (var definition in allDefinitions)
        {
            var appliedState = await this._appliedStateRepository.GetAsync(userId, profileNo, definition.Id);

            // A disabled pick used to be skipped before the applied state was even looked up, so a pick
            // the caller had already applied vanished from the list the moment it was disabled -- while
            // its alarms stayed. This page is the only place Remove exists, so the user was left with
            // alarms they could not un-apply and nothing to say where they came from; an admin disabling
            // a global pick did that to everyone who had applied it. Disabled means "cannot be applied",
            // not "cannot be undone", so it stays listed while it still owns something. See #508.
            if (!definition.Enabled && appliedState is null)
            {
                continue;
            }

            // Verify tracked alarms still exist — if all deleted manually, clear applied state
            if (appliedState?.TrackedUids is { Count: > 0 })
            {
                // Against the type the alarms were CREATED as, not the definition's current one. Editing
                // an applied pick's alarm type -- a plain dropdown in the edit dialog -- made this look
                // the monster uids up among the raids, find none, conclude the user had deleted them all
                // and drop the applied state. The alarms stayed behind with nothing owning them and no
                // Remove button, because the card then read as never applied. RemoveAsync already keys
                // off the stored type for exactly this reason. See #541.
                var trackedType = string.IsNullOrEmpty(appliedState.AlarmType)
                    ? definition.AlarmType
                    : appliedState.AlarmType;

                var remaining = await this.CountRemainingUidsAsync(userId, trackedType, appliedState.TrackedUids);
                if (remaining == 0)
                {
                    // All alarms were deleted manually — clean up stale applied state
                    await this._appliedStateRepository.DeleteAsync(userId, profileNo, definition.Id);
                    appliedState = null;
                }
                else if (remaining < appliedState.TrackedUids.Count)
                {
                    // Some alarms were deleted — update the tracked UIDs to only valid ones
                    appliedState.TrackedUids = await this.GetValidUidsAsync(userId, trackedType, appliedState.TrackedUids);
                    await this._appliedStateRepository.CreateOrUpdateAsync(appliedState);
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

    public async Task<QuickPickDefinition?> GetByIdAsync(string id) => await this._definitionRepository.GetByIdAsync(id);

    /// <summary>
    /// Refuses a definition whose filters the alarm endpoints would reject.
    /// </summary>
    /// <remarks>
    /// Apply validates the alarm it builds (#565), but the definition itself was never checked, so an
    /// admin could save a global pick holding a value no alarm accepts -- a PVP league of 1000, say -- and
    /// every user who applied it got the refusal instead. Failing at save time puts the error in front of
    /// the person who can fix it. See #604.
    /// </remarks>
    private static void EnsureFiltersAreUsable(QuickPickDefinition definition)
    {
        // Nothing to check, and checking anyway broke seeding. Two built-ins -- all-invasions and
        // invasion-leader -- carry no filters on purpose because ApplyInvasionAsync fans them out across
        // grunt types at apply time, so the sample alarm built here has no grunt_type and the Create DTO
        // requires one. SeedDefaultsAsync goes through this method, so it threw partway and left a
        // partial preset list behind both entry points that call it. Apply-time validation (#565) still
        // covers the alarm that actually gets built. See #637.
        if (definition.Filters is null || definition.Filters.Count == 0)
        {
            return;
        }

        try
        {
            BuildSampleAlarm(definition, 0, new QuickPickApplyRequest());
        }
        catch (AlarmValidationException ex)
        {
            throw new AlarmValidationException(
                $"This quick pick cannot be saved: {ex.Message}");
        }
    }
    /// <summary>
    /// Returns the pick only if <paramref name="userId"/> is allowed to see it: global picks are public,
    /// user picks are visible to their owner alone.
    /// </summary>
    public async Task<QuickPickDefinition?> GetVisibleByIdAsync(string userId, string id) =>
        await this.LoadDefinitionAsync(userId, id);

    public async Task<QuickPickDefinition> SaveAdminPickAsync(QuickPickDefinition definition)
    {
        EnsureFiltersAreUsable(definition);

        definition.Id = await this.EnsureIdAsync(definition);

        // Given an id, this used to convert whatever it found into a global pick -- including a private
        // one belonging to somebody else, which then appeared for every user and vanished from its
        // owner's list. SaveUserPickAsync has always had this guard. See #631.
        var existing = await this._definitionRepository.GetByIdAsync(definition.Id);
        if (existing is not null
            && !string.Equals(existing.Scope, "global", StringComparison.OrdinalIgnoreCase))
        {
            throw new AlarmValidationException(
                "That quick pick belongs to a user. Publish a copy instead of converting theirs.");
        }

        definition.Scope = "global";
        definition.OwnerUserId = null;

        await this._definitionRepository.CreateOrUpdateAsync(definition);

        return definition;
    }

    /// <summary>
    /// Returns the definition's id, generating a slug from its name when the caller supplied none.
    /// <para>
    /// The create dialog has no id field and sends <c>""</c>, which the repository stored verbatim. Every
    /// id-bearing route then collapsed to <c>/api/quick-picks/</c> and could not match: delete returned 405,
    /// apply returned 404, and the pick could not be removed through any API path.
    /// </para>
    /// </summary>
    private async Task<string> EnsureIdAsync(QuickPickDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Id))
        {
            return definition.Id;
        }

        var slug = Slugify(definition.Name);
        if (slug.Length == 0)
        {
            slug = "quick-pick";
        }

        // The id column is 50 characters and the name column is 200, so a perfectly legal name produced
        // an id that could not be stored and the create came back as an opaque 500. Room is left for the
        // longest suffix the collision loop can append. See #555.
        slug = Truncate(slug, MaxIdLength - SuffixAllowance);

        // Names are not unique, so settle collisions with a counter before falling back to a guid.
        var candidate = slug;
        for (var attempt = 2; attempt <= 50; attempt++)
        {
            if (await this._definitionRepository.GetByIdAsync(candidate) is null)
            {
                return candidate;
            }

            candidate = $"{slug}-{attempt}";
        }

        return Truncate($"{slug}-{Guid.NewGuid():N}", MaxIdLength);
    }

    /// <summary>The quick_pick_definitions.id column width.</summary>
    private const int MaxIdLength = 50;

    /// <summary>Room for the "-2".."-50" the collision loop appends, and for a guid tail if it gets there.</summary>
    private const int SuffixAllowance = 4;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd('-');

    private static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-');

        // Collapse runs of separators so "Hundo  IV!" becomes "hundo-iv".
        var slug = string.Concat(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    public async Task<QuickPickDefinition> SaveUserPickAsync(string userId, QuickPickDefinition definition)
    {
        EnsureFiltersAreUsable(definition);

        // CreateOrUpdateAsync upserts on Id alone, and the Id arrives from the request body. Without this
        // check a user could post a global pick's well-known Id (hundo, nundo, raid-5star, ...) and the
        // upsert would rewrite that row -- flipping it to scope=user under their ownership and removing it
        // from every other user's list. The same applies to another user's private pick.
        if (!string.IsNullOrEmpty(definition.Id))
        {
            var existing = await this._definitionRepository.GetByIdAsync(definition.Id);
            if (existing != null && !IsOwnedBy(existing, userId))
            {
                throw new UnauthorizedAccessException(
                    $"Quick pick '{definition.Id}' already exists and is not yours.");
            }
        }

        definition.Id = await this.EnsureIdAsync(definition);
        definition.Scope = "user";
        definition.OwnerUserId = userId;

        await this._definitionRepository.CreateOrUpdateAsync(definition);

        return definition;
    }

    /// <summary>A pick is the caller's only when it is user-scoped and owned by them.</summary>
    private static bool IsOwnedBy(QuickPickDefinition definition, string userId) =>
        !string.Equals(definition.Scope, "global", StringComparison.OrdinalIgnoreCase)
        && string.Equals(definition.OwnerUserId, userId, StringComparison.Ordinal);

    public async Task<bool> DeleteAdminPickAsync(string id)
    {
        var existing = await this._definitionRepository.GetByIdAsync(id);
        if (existing == null)
        {
            return false;
        }

        await this._definitionRepository.DeleteAsync(id);

        // A global pick can be applied by anyone, so every user's state for it goes with it. See #470.
        await this._appliedStateRepository.DeleteByQuickPickIdAsync(id);
        return true;
    }

    public async Task<bool> DeleteUserPickAsync(string userId, string id)
    {
        var existing = await this._definitionRepository.GetByIdAndOwnerAsync(id, userId);
        if (existing == null)
        {
            return false;
        }

        await this._definitionRepository.DeleteByIdAndOwnerAsync(id, userId);

        // Only the owner can apply a user-scoped pick, so only their state exists to clear. See #470.
        await this._appliedStateRepository.DeleteByQuickPickIdAsync(id, userId);
        return true;
    }

    public async Task<QuickPickAppliedState> ApplyAsync(
        string userId, int profileNo, string quickPickId, QuickPickApplyRequest request)
    {
        var definition = await this.LoadDefinitionAsync(userId, quickPickId) ?? throw new InvalidOperationException($"Quick pick '{quickPickId}' not found.");

        // A disabled pick now stays listed while it still owns alarms, so that Remove remains reachable
        // (#508). It must not be appliable from there.
        if (!definition.Enabled)
        {
            throw new AlarmValidationException("That quick pick is disabled and cannot be applied.");
        }

        // Applying a pick whose alarm type changed since it was applied would strand the alarms it made
        // under the old type: the new applied state records the new type, and Remove -- which keys off
        // the stored type -- can never reach them again. Refuse rather than strand, and say what to do.
        // Re-apply is the supported way through, because it removes the old alarms first. See #557.
        var existingState = await this._appliedStateRepository.GetAsync(userId, profileNo, quickPickId);
        if (existingState is not null
            && existingState.TrackedUids.Count > 0
            && !string.Equals(existingState.AlarmType, definition.AlarmType, StringComparison.OrdinalIgnoreCase))
        {
            throw new AlarmValidationException(
                "This quick pick still owns alarms of a different type. Remove it first, then apply it again.");
        }

        // Snapshot first: the tracked set is what this apply ADDED, not what the create calls
        // reported. PoracleNG hands back the existing row's uid when a pick matches an alarm the
        // user built by hand, so trusting the reported uid made the pick adopt that alarm - and
        // Remove then deleted it. It also reports no uid at all for an exact duplicate, which
        // recorded a tracked uid of 0 that no lookup could resolve, so the applied state was wiped
        // on the next page load. Diffing sidesteps both. See #468, #469.
        var existingUids = await this.ExistingUidsAsync(userId, profileNo, definition.AlarmType);

        var reportedUids = definition.AlarmType switch
        {
            "monster" => await this.ApplyMonsterAsync(userId, profileNo, definition, request),
            "raid" => await this.ApplyRaidAsync(userId, profileNo, definition, request),
            "egg" => await this.ApplyEggAsync(userId, profileNo, definition, request),
            "quest" => await this.ApplyQuestAsync(userId, profileNo, definition, request),
            "invasion" => await this.ApplyInvasionAsync(userId, profileNo, definition, request),
            "lure" => await this.ApplyLureAsync(userId, profileNo, definition, request),
            "nest" => await this.ApplyNestAsync(userId, profileNo, definition, request),
            "gym" => await this.ApplyGymAsync(userId, profileNo, definition, request),
            "maxbattle" => await this.ApplyMaxBattleAsync(userId, profileNo, definition, request),
            _ => throw new InvalidOperationException($"Unknown alarm type '{definition.AlarmType}'."),
        };

        var afterUids = await this.ExistingUidsAsync(userId, profileNo, definition.AlarmType);
        var addedUids = afterUids.Except(existingUids).ToList();
        var displacedUids = existingUids.Except(afterUids).ToList();

        // A uid that appeared is not automatically ours. When a pick matches an alarm the user built by
        // hand, PoracleNG does not add a row - it RE-KEYS theirs, so the old uid disappears and a new one
        // appears and the diff looks identical to a creation. Removing the pick then deleted the user's
        // alarm. If anything was displaced we claim nothing: an untracked leftover is recoverable by
        // hand, a deleted alarm is not. See #469.
        var trackedUids = displacedUids.Count == 0 ? addedUids : [];

        if (displacedUids.Count > 0)
        {
            LogQuickPickDisplaced(this._logger, quickPickId, displacedUids.Count, addedUids.Count);
        }

        LogQuickPickTracking(this._logger, quickPickId, reportedUids.Count, trackedUids.Count);

        // Applying an already-applied pick adds nothing, because PoracleNG dedups -- so the diff is
        // empty and writing it verbatim handed the pick an empty tracked list while its alarms were
        // still there. Remove then answered 204 and deleted nothing, and the alarms were left with no
        // way to attribute them. Keep what the pick already owned and add whatever this run created.
        // See #542.
        var previouslyTracked = await this._appliedStateRepository.GetAsync(userId, profileNo, quickPickId);
        var stillOwned = previouslyTracked?.TrackedUids?.Where(afterUids.Contains) ?? [];

        var appliedState = new QuickPickAppliedState
        {
            UserId = userId,
            ProfileNo = profileNo,
            QuickPickId = quickPickId,
            AlarmType = definition.AlarmType,
            AppliedAt = DateTime.UtcNow,
            ExcludePokemonIds = request.ExcludePokemonIds,
            TrackedUids = [.. stillOwned.Union(trackedUids)],
        };

        await this._appliedStateRepository.CreateOrUpdateAsync(appliedState);

        LogQuickPickApplied(this._logger, quickPickId, userId, profileNo, trackedUids.Count);

        return appliedState;
    }

    /// <summary>
    /// The uids the user currently holds for an alarm type. Used either side of an apply so the
    /// pick claims only the rows it actually created.
    /// </summary>
    private async Task<List<int>> ExistingUidsAsync(string userId, int profileNo, string alarmType) => alarmType switch
    {
        "monster" => [.. (await this._monsterService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "raid" => [.. (await this._raidService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "egg" => [.. (await this._eggService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "quest" => [.. (await this._questService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "invasion" => [.. (await this._invasionService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "lure" => [.. (await this._lureService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "nest" => [.. (await this._nestService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "gym" => [.. (await this._gymService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        "maxbattle" => [.. (await this._maxBattleService.GetByUserAsync(userId, profileNo)).Select(x => x.Uid)],
        _ => [],
    };

    public async Task<QuickPickAppliedState> ReapplyAsync(
        string userId, int profileNo, string quickPickId, QuickPickApplyRequest request)
    {
        // Remove used to run first unconditionally. Once apply grew guards -- a disabled pick, a filter
        // the alarm model refuses, an alarm type an admin has switched off -- a refused re-apply
        // destroyed the alarms and created nothing in their place, while the error read as "nothing
        // happened". The pick also dropped off the list, taking the Remove button with it. Everything
        // that can refuse this is checked BEFORE anything is deleted. See #531.
        await this.EnsureApplicableAsync(userId, profileNo, quickPickId, request);

        await this.RemoveAsync(userId, profileNo, quickPickId);
        return await this.ApplyAsync(userId, profileNo, quickPickId, request);
    }

    /// <summary>
    /// Runs everything that can refuse an apply, without writing anything.
    /// </summary>
    /// <remarks>
    /// Builds the alarms the apply would build and validates them, which is where an impossible filter
    /// surfaces. Building is side-effect free, so this is a genuine dry run rather than a partial apply.
    /// </remarks>
    private async Task EnsureApplicableAsync(
        string userId, int profileNo, string quickPickId, QuickPickApplyRequest request)
    {
        var definition = await this.LoadDefinitionAsync(userId, quickPickId)
            ?? throw new InvalidOperationException($"Quick pick '{quickPickId}' not found.");

        if (!definition.Enabled)
        {
            throw new AlarmValidationException("That quick pick is disabled and cannot be applied.");
        }

        // The same gate the alarm services apply, so a type an admin has switched off refuses here
        // rather than half-way through, after the deletes.
        var disableKey = DisableFeatureKeys.ByTrackingType.TryGetValue(definition.AlarmType, out var key)
            ? key
            : null;
        if (disableKey is not null)
        {
            await this._featureGate.EnsureEnabledAsync(disableKey);
        }

        // The alarm-type guard runs here too, not just in ApplyAsync. Re-apply deletes before it applies,
        // so a pick whose type changed since it was applied lost its alarms AND its applied state before
        // the refusal ever fired -- the deletes are committed and the error reads as "nothing happened".
        // See #579.
        var applied = await this._appliedStateRepository.GetAsync(userId, profileNo, quickPickId);
        if (applied is not null
            && applied.TrackedUids.Count > 0
            && !string.Equals(applied.AlarmType, definition.AlarmType, StringComparison.OrdinalIgnoreCase))
        {
            throw new AlarmValidationException(
                "This quick pick still owns alarms of a different type. Remove it first, then apply it again.");
        }

        // Building throws for a filter the alarm model refuses. Nothing is sent anywhere.
        BuildSampleAlarm(definition, profileNo, request);
    }

    /// <summary>Builds one alarm of the definition's type purely to run its validation.</summary>
    /// <summary>Deserializes a definition's filters onto its model and validates them, writing nothing.</summary>
    private static void BuildFromFilters<T>(Dictionary<string, object?> filters)
        where T : new()
    {
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var alarm = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        EnsureValidAlarm(alarm!);
    }

    private static void BuildSampleAlarm(
        QuickPickDefinition definition, int profileNo, QuickPickApplyRequest request)
    {
        switch (definition.AlarmType)
        {
            case "monster":
                BuildMonster(definition.Filters, 1, profileNo, request);
                break;
            case "raid":
                BuildRaid(definition.Filters, profileNo, request);
                break;
            case "maxbattle":
                BuildMaxBattle(definition.Filters, profileNo, request);
                break;
            // The rest deserialize their filters straight onto the model. That IS validated on the way
            // through -- but only after RemoveAsync has already deleted the alarms, so a re-apply of a pick
            // with a bad filter destroyed them and created nothing. And a definition holding such a filter
            // could still be SAVED, because the save check runs the same dry run. Six of the nine types
            // fell through here. See #607, #608.
            case "egg":
                BuildFromFilters<Egg>(definition.Filters);
                break;
            case "quest":
                BuildFromFilters<Quest>(definition.Filters);
                break;
            case "invasion":
                BuildFromFilters<Invasion>(definition.Filters);
                break;
            case "lure":
                BuildFromFilters<Lure>(definition.Filters);
                break;
            case "nest":
                BuildFromFilters<Nest>(definition.Filters);
                break;
            case "gym":
                BuildFromFilters<Gym>(definition.Filters);
                break;
            default:
                // An alarm type this build does not know. Apply will fail on it anyway.
                break;
        }
    }

    public async Task<bool> RemoveAsync(string userId, int profileNo, string quickPickId)
    {
        var appliedState = await this._appliedStateRepository.GetAsync(userId, profileNo, quickPickId);

        if (appliedState == null)
        {
            return false;
        }

        // Use alarm type stored at apply time — works even if the definition was deleted
        var alarmType = appliedState.AlarmType;

        // Delete each tracked alarm row
        foreach (var uid in appliedState.TrackedUids)
        {
            switch (alarmType)
            {
                case "monster":
                    await this._monsterService.DeleteAsync(userId, uid);
                    break;
                case "raid":
                    await this._raidService.DeleteAsync(userId, uid);
                    break;
                case "egg":
                    await this._eggService.DeleteAsync(userId, uid);
                    break;
                case "quest":
                    await this._questService.DeleteAsync(userId, uid);
                    break;
                case "invasion":
                    await this._invasionService.DeleteAsync(userId, uid);
                    break;
                case "lure":
                    await this._lureService.DeleteAsync(userId, uid);
                    break;
                case "nest":
                    await this._nestService.DeleteAsync(userId, uid);
                    break;
                case "gym":
                    await this._gymService.DeleteAsync(userId, uid);
                    break;
                case "maxbattle":
                    await this._maxBattleService.DeleteAsync(userId, uid);
                    break;
                default:
                    break;
            }
        }

        // Delete the applied state
        await this._appliedStateRepository.DeleteAsync(userId, profileNo, quickPickId);

        LogQuickPickRemoved(this._logger, quickPickId, userId, profileNo, appliedState.TrackedUids.Count);

        return true;
    }

    public Task<IEnumerable<QuickPickDefinition>> GetDefaultPicksAsync() => Task.FromResult<IEnumerable<QuickPickDefinition>>(Defaults);

    public async Task SeedDefaultsAsync()
    {
        // Delete any existing global quick picks so we can re-seed cleanly
        var existingGlobal = await this._definitionRepository.GetAllGlobalAsync();
        var existingCount = existingGlobal.Count;

        // Applied state goes with the definitions it belongs to, exactly as DeleteAdminPickAsync does
        // (#470). Without this, every user kept a row pointing at a definition that no longer exists:
        // GetAllAsync iterates definitions, so the state was never listed and never cleaned, and the
        // alarms it owned lost their Remove button for good. Worse, a later pick generating a colliding
        // slug re-attached that state, and its trackedUids then named unrelated alarms. See #630.
        foreach (var stale in existingGlobal)
        {
            await this._appliedStateRepository.DeleteByQuickPickIdAsync(stale.Id);
        }

        await this._definitionRepository.DeleteAllGlobalAsync();

        LogSeedingDefaults(this._logger, Defaults.Count, existingCount);

        foreach (var definition in Defaults)
        {
            await this.SaveAdminPickAsync(definition);
        }
    }

    // --- Private helpers ---

    private async Task<QuickPickDefinition?> LoadDefinitionAsync(string userId, string quickPickId)
    {
        // Global picks are readable by everyone. The unscoped lookup returns rows of ANY scope, so it must
        // be narrowed to global here -- otherwise applying another user's private pick succeeds and creates
        // real alarms from filters the caller was never allowed to see.
        var definition = await this._definitionRepository.GetByIdAsync(quickPickId);
        if (definition != null && string.Equals(definition.Scope, "global", StringComparison.OrdinalIgnoreCase))
        {
            return definition;
        }

        // Otherwise it must be the caller's own pick.
        return await this._definitionRepository.GetByIdAndOwnerAsync(quickPickId, userId);
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

    /// <summary>
    /// Runs the checks a model-bound POST would have run on an alarm built from quick-pick filters.
    /// </summary>
    /// <remarks>
    /// Applying a pick builds the alarm in code and hands it straight to the alarm service, so none of the
    /// DataAnnotations that ASP.NET applies to a bound request ever ran: values POST /api/monsters refuses
    /// with a 400 -- minIv 500, say -- were persisted verbatim, producing an alarm that can never match
    /// anything and notifies nothing, with no error anywhere. A quick pick is not a side door around the
    /// model's own rules. See #507.
    /// </remarks>
    private static void EnsureValidAlarm(object alarm)
    {
        // Against the *Create DTO, because that is where the [Range] and [StringLength] attributes live --
        // the domain models carry none, so validating those found nothing and a pick holding minIv 500 was
        // applied verbatim. Same trap as #548. See #565.
        var validationTarget = AsCreateDto(alarm) ?? alarm;

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                validationTarget, new ValidationContext(validationTarget), results, validateAllProperties: true))
        {
            throw new AlarmValidationException(
                "This quick pick holds a filter value the alarm does not accept: "
                + (results[0].ErrorMessage ?? "value out of range"));
        }

        if (alarm is Monster monster)
        {
            var inverted = MonsterRangeValidator.Validate(monster);
            if (inverted is not null)
            {
                throw new AlarmValidationException($"This quick pick holds an impossible filter: {inverted}");
            }
        }
    }
    /// <summary>Re-reads a built alarm as the DTO the POST endpoints bind, which carries the rules.</summary>
    private static object? AsCreateDto(object alarm)
    {
        var json = JsonSerializer.Serialize(alarm, alarm.GetType(), SnakeCaseOptions);

        return alarm switch
        {
            Monster => JsonSerializer.Deserialize<MonsterCreate>(json, SnakeCaseOptions),
            Raid => JsonSerializer.Deserialize<RaidCreate>(json, SnakeCaseOptions),
            Egg => JsonSerializer.Deserialize<EggCreate>(json, SnakeCaseOptions),
            Quest => JsonSerializer.Deserialize<QuestCreate>(json, SnakeCaseOptions),
            Invasion => JsonSerializer.Deserialize<InvasionCreate>(json, SnakeCaseOptions),
            Lure => JsonSerializer.Deserialize<LureCreate>(json, SnakeCaseOptions),
            Nest => JsonSerializer.Deserialize<NestCreate>(json, SnakeCaseOptions),
            Gym => JsonSerializer.Deserialize<GymCreate>(json, SnakeCaseOptions),
            MaxBattle => JsonSerializer.Deserialize<MaxBattleCreate>(json, SnakeCaseOptions),
            _ => null,
        };
    }

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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

        // Overlay the quick pick filters on top of the defaults.
        // Whitelist safe filter properties to prevent setting Id, Uid, or ProfileNo via reflection.
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var overrides = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                if (!SafeMonsterFilterKeys.Contains(key))
                {
                    continue;
                }

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

        EnsureValidAlarm(monster);

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

        // PoracleNG treats pokemon_id=0 as "everything" and overrides all filters.
        // Use 9000 ("any pokemon") to preserve level-based filtering.
        if (raid.PokemonId == 0)
        {
            raid.PokemonId = 9000;
        }

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

        EnsureValidAlarm(raid);

        return raid;
    }

    // --- Egg ---

    private async Task<List<int>> ApplyEggAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var egg = JsonSerializer.Deserialize<Egg>(json, JsonOptions) ?? new Egg();
        EnsureValidAlarm(egg);

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
        EnsureValidAlarm(quest);

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

    // Fan-out targets for the "invasion-leader" quick pick — three distinct grunt_type
    // values in PoracleNG. A single filter dict can only set one gruntType, so we create
    // one alarm per leader rather than complicating the QuickPick schema. Giovanni is
    // deliberately excluded (separate `invasion-giovanni` pick, since he spawns from the
    // Super Rocket Radar only).
    private static readonly IReadOnlyList<string> LeaderFanOutGruntTypes = InvasionGruntTypes.Leaders;

    private async Task<List<int>> ApplyInvasionAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        // "All Invasions" shipped with empty filters, so BuildInvasion produced grunt_type "" and every
        // apply failed with a 500. PoracleNG has no catch-all, so "all" has to be a fan-out too. See #416.
        var fanOut = definition.Id switch
        {
            "all-invasions" => InvasionGruntTypes.All,
            "invasion-leader" => LeaderFanOutGruntTypes,
            _ => null
        };

        if (fanOut != null)
        {
            var invasions = fanOut.Select(gt => BuildInvasion(definition.Filters, profileNo, request, gt)).ToList();
            var created = await this._invasionService.BulkCreateAsync(userId, invasions);
            return [.. created.Select(i => i.Uid)];
        }

        var invasion = BuildInvasion(definition.Filters, profileNo, request, null);
        var singleCreated = await this._invasionService.CreateAsync(userId, invasion);
        return [singleCreated.Uid];
    }

    private static Invasion BuildInvasion(
        Dictionary<string, object?> filters, int profileNo, QuickPickApplyRequest request, string? gruntTypeOverride)
    {
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var invasion = JsonSerializer.Deserialize<Invasion>(json, JsonOptions) ?? new Invasion();

        invasion.ProfileNo = profileNo;

        if (gruntTypeOverride != null)
        {
            invasion.GruntType = gruntTypeOverride;
        }

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

        EnsureValidAlarm(invasion);

        return invasion;
    }

    // --- Lure ---

    private async Task<List<int>> ApplyLureAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(definition.Filters, JsonOptions);
        var lure = JsonSerializer.Deserialize<Lure>(json, JsonOptions) ?? new Lure();
        EnsureValidAlarm(lure);

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
        EnsureValidAlarm(nest);

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
        EnsureValidAlarm(gym);

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

    // --- MaxBattle ---

    private async Task<List<int>> ApplyMaxBattleAsync(
        string userId, int profileNo, QuickPickDefinition definition, QuickPickApplyRequest request)
    {
        var pokemonId = GetFilterInt(definition.Filters, "pokemonId");
        var level = GetFilterInt(definition.Filters, "level");

        if (pokemonId == 9000 && level == 9000)
        {
            // Level-based: create one alarm per level (1-5 normal, 7-8 gmax)
            var maxBattles = new List<MaxBattle>();
            foreach (var lvl in new[] { 1, 2, 3, 4, 5, 7, 8 })
            {
                var mb = BuildMaxBattle(definition.Filters, profileNo, request);
                mb.PokemonId = 9000;
                mb.Level = lvl;
                mb.Gmax = lvl >= 7 ? 1 : 0;
                maxBattles.Add(mb);
            }

            var created = await this._maxBattleService.BulkCreateAsync(userId, maxBattles);
            return [.. created.Select(m => m.Uid)];
        }
        else
        {
            // Specific Pokemon or specific level — single row
            var maxBattle = BuildMaxBattle(definition.Filters, profileNo, request);
            var created = await this._maxBattleService.CreateAsync(userId, maxBattle);
            return [created.Uid];
        }
    }

    private static MaxBattle BuildMaxBattle(Dictionary<string, object?> filters, int profileNo, QuickPickApplyRequest request)
    {
        var json = JsonSerializer.Serialize(filters, JsonOptions);
        var maxBattle = JsonSerializer.Deserialize<MaxBattle>(json, JsonOptions) ?? new MaxBattle();

        maxBattle.ProfileNo = profileNo;

        if (request.Distance.HasValue)
        {
            maxBattle.Distance = request.Distance.Value;
        }

        if (request.Clean.HasValue)
        {
            maxBattle.Clean = request.Clean.Value;
        }

        if (request.Template != null)
        {
            maxBattle.Template = request.Template;
        }

        EnsureValidAlarm(maxBattle);

        return maxBattle;
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

    private async Task<int> CountRemainingUidsAsync(string userId, string alarmType, List<int> uids)
    {
        var count = 0;
        foreach (var uid in uids)
        {
            var exists = alarmType switch
            {
                "monster" => await this._monsterService.GetByUidAsync(userId, uid) != null,
                "raid" => await this._raidService.GetByUidAsync(userId, uid) != null,
                "egg" => await this._eggService.GetByUidAsync(userId, uid) != null,
                "quest" => await this._questService.GetByUidAsync(userId, uid) != null,
                "invasion" => await this._invasionService.GetByUidAsync(userId, uid) != null,
                "lure" => await this._lureService.GetByUidAsync(userId, uid) != null,
                "nest" => await this._nestService.GetByUidAsync(userId, uid) != null,
                "gym" => await this._gymService.GetByUidAsync(userId, uid) != null,
                "maxbattle" => await this._maxBattleService.GetByUidAsync(userId, uid) != null,
                _ => false,
            };
            if (exists)
            {
                count++;
            }
        }

        return count;
    }

    private async Task<List<int>> GetValidUidsAsync(string userId, string alarmType, List<int> uids)
    {
        var valid = new List<int>();
        foreach (var uid in uids)
        {
            var exists = alarmType switch
            {
                "monster" => await this._monsterService.GetByUidAsync(userId, uid) != null,
                "raid" => await this._raidService.GetByUidAsync(userId, uid) != null,
                "egg" => await this._eggService.GetByUidAsync(userId, uid) != null,
                "quest" => await this._questService.GetByUidAsync(userId, uid) != null,
                "invasion" => await this._invasionService.GetByUidAsync(userId, uid) != null,
                "lure" => await this._lureService.GetByUidAsync(userId, uid) != null,
                "nest" => await this._nestService.GetByUidAsync(userId, uid) != null,
                "gym" => await this._gymService.GetByUidAsync(userId, uid) != null,
                "maxbattle" => await this._maxBattleService.GetByUidAsync(userId, uid) != null,
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
        new() { Id = "invasion-leader", Name = "Rocket Leaders", Description = "Track Sierra, Cliff, and Arlo encounters", Icon = "supervisor_account", Category = "Invasions", AlarmType = "invasion", SortOrder = 51, Filters = [] },
        new() { Id = "invasion-giovanni", Name = "Giovanni", Description = "Track Giovanni boss encounters", Icon = "military_tech", Category = "Invasions", AlarmType = "invasion", SortOrder = 52, Filters = new() { ["gruntType"] = "giovanni" } },

        // ── Lures ──
        new() { Id = "lure-glacial", Name = "Glacial Lures", Description = "Track Glacial Lure Modules at PokeStops", Icon = "ac_unit", Category = "Lures", AlarmType = "lure", SortOrder = 60, Filters = new() { ["lureId"] = 502 } },
        new() { Id = "lure-magnetic", Name = "Magnetic Lures", Description = "Track Magnetic Lure Modules at PokeStops", Icon = "bolt", Category = "Lures", AlarmType = "lure", SortOrder = 61, Filters = new() { ["lureId"] = 501 } },
        new() { Id = "lure-mossy", Name = "Mossy Lures", Description = "Track Mossy Lure Modules at PokeStops", Icon = "eco", Category = "Lures", AlarmType = "lure", SortOrder = 62, Filters = new() { ["lureId"] = 503 } },
        new() { Id = "lure-rainy", Name = "Rainy Lures", Description = "Track Rainy Lure Modules at PokeStops", Icon = "water_drop", Category = "Lures", AlarmType = "lure", SortOrder = 63, Filters = new() { ["lureId"] = 504 } },
        new() { Id = "lure-golden", Name = "Golden Lures", Description = "Track Golden Lure Modules at PokeStops", Icon = "stars", Category = "Lures", AlarmType = "lure", SortOrder = 64, Filters = new() { ["lureId"] = 505 } },
    ];

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Quick pick {QuickPickId} matched {DisplacedCount} alarm(s) the user already had; claiming none of the {AddedCount} resulting row(s) so removing the pick cannot delete them.")]
    private static partial void LogQuickPickDisplaced(ILogger logger, string quickPickId, int displacedCount, int addedCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied quick pick '{QuickPickId}' for user {UserId} profile {ProfileNo}, created {Count} alarm(s).")]
    private static partial void LogQuickPickApplied(ILogger logger, string quickPickId, string userId, int profileNo, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed quick pick '{QuickPickId}' for user {UserId} profile {ProfileNo}, deleted {Count} alarm(s).")]
    private static partial void LogQuickPickRemoved(ILogger logger, string quickPickId, string userId, int profileNo, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding {Count} default quick picks (replaced {Existing} existing).")]
    private static partial void LogSeedingDefaults(ILogger logger, int count, int existing);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Quick pick {QuickPickId}: PoracleNG named {ReportedCount} uid(s), {TrackedCount} of which are new rows this apply created.")]
    private static partial void LogQuickPickTracking(ILogger logger, string quickPickId, int reportedCount, int trackedCount);
}
