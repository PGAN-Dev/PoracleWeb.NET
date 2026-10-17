namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A game account (player id) linked to a human, used by personal shiny alerts.
/// </summary>
/// <remarks>
/// Player ids are case-sensitive. Never lowercase, trim or otherwise normalise <see cref="PlayerId"/>.
/// </remarks>
public sealed record PlayerAccount(string PlayerId, string? Label, DateTimeOffset CreatedAt);
