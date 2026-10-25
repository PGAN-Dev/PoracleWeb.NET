using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>Answers "who are this person's humans" for the current request.</summary>
public interface ILinkedAccount
{
    /// <summary>
    /// The caller's link resolved against PoracleNG. With no link, or with linking switched off, this is
    /// <see cref="LinkedPair.Single"/> and nothing is read from PoracleNG. A failed human read throws.
    /// </summary>
    public Task<LinkedPair> ResolveAsync(string userId);

    /// <summary>Drops the cached link and human reads for this id and, when known, the other id of its link.</summary>
    public void Invalidate(string humanId);
}
