using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>Creates the missing side's human from the caller's, without alerts.</summary>
public interface ILinkedHumanProvisioner
{
    /// <exception cref="LinkRefusalException">ProvisionFailed, after anything half-created was removed.</exception>
    public Task CreateMirrorAsync(string sourceHumanId, LinkSide newSide, string newId, string newName);

    /// <summary>Deletes the human at <paramref name="newId"/> only if it is still the one this process created.</summary>
    public Task RollBackAsync(string newId, LinkSide newSide, string newName);
}
