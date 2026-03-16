namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IMasterDataService
{
    Task<string?> GetPokemonDataAsync();
    Task<string?> GetItemDataAsync();
    Task RefreshCacheAsync();
}
