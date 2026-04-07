namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IGolbatApiProxy
{
    public Task<IReadOnlyList<int>> GetAvailablePokemonAsync(CancellationToken cancellationToken = default);
}
