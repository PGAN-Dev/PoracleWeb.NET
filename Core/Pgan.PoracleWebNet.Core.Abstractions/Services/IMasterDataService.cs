namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IMasterDataService
{
    public Task<string?> GetPokemonDataAsync();
    public Task<string?> GetItemDataAsync();
    public Task RefreshCacheAsync();
}
