using Pgan.PoracleWebNet.Core.Models.Pvp;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IMasterDataService
{
    public Task<string?> GetPokemonDataAsync();
    public Task<string?> GetItemDataAsync();
    public Task RefreshCacheAsync();

    /// <summary>
    /// Return base stats for a species+form. Falls back to form=0 when no form-specific
    /// entry exists. Returns null when the species is unknown or masterdata failed to load.
    /// </summary>
    public Task<BaseStats?> GetBaseStatsAsync(int pokemonId, int form);
}
