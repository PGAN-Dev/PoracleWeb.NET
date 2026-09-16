namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// The invasion <c>grunt_type</c> values the upstream PoracleNG will actually accept, read from its own
/// grunt masterdata.
/// </summary>
public interface IInvasionGruntNameService
{
    /// <summary>
    /// The accepted names, lowercased, plus the two catch-alls. Empty when the list could not be read —
    /// which routes every invasion write to v1, exactly as they go today.
    /// </summary>
    Task<IReadOnlySet<string>> GetAsync(CancellationToken cancellationToken = default);
}
