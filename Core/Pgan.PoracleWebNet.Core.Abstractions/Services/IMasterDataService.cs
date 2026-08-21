using Pgan.PoracleWebNet.Core.Models.Pvp;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IMasterDataService
{
    public Task<string?> GetPokemonDataAsync();
    public Task<string?> GetItemDataAsync();

    /// <summary>Move ID to name map (e.g. <c>{"13":"Wrap"}</c>), sourced from the masterfile.</summary>
    public Task<string?> GetMoveDataAsync();

    /// <summary>
    /// The raw masterfile monster map keyed <c>"{pokemonId}_{formId}"</c> (names, types, forms,
    /// stats, evolutions). English only - it is the fallback for when PoracleNG cannot serve its
    /// localized equivalent.
    /// </summary>
    public Task<string?> GetMonsterDataAsync();
    public Task RefreshCacheAsync();

    /// <summary>
    /// Return base stats for a species+form. Falls back to form=0 when no form-specific
    /// entry exists. Returns null when the species is unknown or masterdata failed to load.
    /// </summary>
    public Task<BaseStats?> GetBaseStatsAsync(int pokemonId, int form);
}
