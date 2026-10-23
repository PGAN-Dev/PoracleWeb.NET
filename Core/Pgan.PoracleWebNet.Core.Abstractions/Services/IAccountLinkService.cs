using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// The one place a verified identity becomes a link. Callers hand over an identity they have already
/// verified (Telegram widget, Discord OAuth); this checks the pairing rules and writes the row.
/// </summary>
public interface IAccountLinkService
{
    /// <summary>Links the caller's human to <paramref name="newId"/> on <paramref name="newSide"/>.</summary>
    /// <exception cref="LinkRefusalException">The pairing is refused; <c>Code</c> says why.</exception>
    public Task<AccountLink> LinkAsync(string callerHumanId, LinkSide newSide, string newId, string newName);

    /// <summary>Removes the link holding this id. False when there was none.</summary>
    public Task<bool> UnlinkAsync(string humanId);

    /// <exception cref="LinkRefusalException">There is no link, or that side's human does not exist.</exception>
    public Task SetDefaultAsync(string humanId, LinkSide side);
}

/// <summary>Creates the missing side's human from the caller's, without alerts.</summary>
public interface ILinkedHumanProvisioner
{
    /// <exception cref="LinkRefusalException">ProvisionFailed, after anything half-created was removed.</exception>
    public Task CreateMirrorAsync(string sourceHumanId, LinkSide newSide, string newId, string newName);

    /// <summary>Deletes the human at <paramref name="newId"/> only if it is still the one this process created.</summary>
    public Task RollBackAsync(string newId, LinkSide newSide, string newName);
}
