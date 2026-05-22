using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Returns the canonical raid-level list. Currently sources from a baked-in
/// snapshot of the WatWowMap masterfile; a future enhancement can refresh
/// this list from the live masterfile URL (see comments in `GetAllAsync`)
/// and persist to disk under DATA_DIR.
///
/// The baked-in list IS the fallback when an upstream fetch fails. Frontend
/// callers must never assume this list is complete — they always allow
/// arbitrary integers via the custom-level input.
/// </summary>
public class RaidLevelService : IRaidLevelService
{
    // Mirror of the masterfile's raid_{N} / raid_{N}_plural keys as of writing.
    // Source: https://github.com/WatWowMap/Masterfile-Generator (master-latest-poracle-v2.json)
    // When upstream adds raid_20+, append entries here and bump the i18n keys in
    // RAIDS.LEVEL.* — or wire up the live fetch documented below.
    private static readonly IReadOnlyList<RaidLevelInfo> BakedIn = new RaidLevelInfo[]
    {
        new() { Value = 1, Category = "star", Name = "1 Star Raid", NamePlural = "1 Star Raids" },
        new() { Value = 2, Category = "star", Name = "2 Star Raid", NamePlural = "2 Star Raids" },
        new() { Value = 3, Category = "star", Name = "3 Star Raid", NamePlural = "3 Star Raids" },
        new() { Value = 4, Category = "star", Name = "4 Star Raid", NamePlural = "4 Star Raids" },
        new() { Value = 5, Category = "star", Name = "Legendary Raid", NamePlural = "Legendary Raids" },
        new() { Value = 6, Category = "mega", Name = "Mega Raid", NamePlural = "Mega Raids" },
        new() { Value = 7, Category = "mega", Name = "Mega Legendary Raid", NamePlural = "Mega Legendary Raids" },
        new() { Value = 8, Category = "special", Name = "Ultra Beast Raid", NamePlural = "Ultra Beast Raids" },
        new() { Value = 9, Category = "special", Name = "Elite Raid", NamePlural = "Elite Raids" },
        new() { Value = 10, Category = "special", Name = "Primal Raid", NamePlural = "Primal Raids" },
        new() { Value = 11, Category = "shadow", Name = "1 Shadow Raid", NamePlural = "1 Shadow Raids" },
        new() { Value = 12, Category = "shadow", Name = "2 Shadow Raid", NamePlural = "2 Shadow Raids" },
        new() { Value = 13, Category = "shadow", Name = "3 Shadow Raid", NamePlural = "3 Shadow Raids" },
        new() { Value = 14, Category = "shadow", Name = "4 Shadow Raid", NamePlural = "4 Shadow Raids" },
        new() { Value = 15, Category = "shadow", Name = "5 Shadow Raid", NamePlural = "5 Shadow Raids" },
        new() { Value = 16, Category = "superMega", Name = "4 Super Mega Raid", NamePlural = "4 Super Mega Raids" },
        new() { Value = 17, Category = "superMega", Name = "5 Super Mega Raid", NamePlural = "5 Super Mega Raids" },
        new() { Value = 18, Category = "coordinated", Name = "Coordinated 1 Raid", NamePlural = "Coordinated 1 Raids" },
        new() { Value = 19, Category = "coordinated", Name = "Coordinated 2 Raid", NamePlural = "Coordinated 2 Raids" },
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<RaidLevelInfo>> GetAllAsync()
    {
        // TODO: fetch + cache from the WatWowMap masterfile URL so new raid types
        // appear without a code change. Recommended approach:
        //   1. HttpClient GET https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/main/master-latest-poracle-v2.json
        //   2. Parse top-level keys matching ^raid_\d+$ + matching `_plural` siblings
        //   3. Persist parsed structure to `${DATA_DIR}/raid-levels.json`
        //   4. Refresh every 24h via a hosted service
        //   5. Fall back to BakedIn on any failure
        // The frontend already tolerates the list being incomplete (custom-level input).
        return Task.FromResult(BakedIn);
    }
}
