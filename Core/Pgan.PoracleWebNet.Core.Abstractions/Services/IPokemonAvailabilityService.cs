namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPokemonAvailabilityService
{
    public Task<IReadOnlyList<int>> GetAvailablePokemonIdsAsync(CancellationToken cancellationToken = default);
}
