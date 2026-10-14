namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPokemonEditCapabilityService
{
    Task<bool> IsPokemonEditEnabledAsync();
}
