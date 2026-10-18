namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A linked game account as the API answers it: an opaque <see cref="Id"/> in place of the player id, which
/// never leaves the server (see <c>PlayerAccountHandles</c>).
/// </summary>
public sealed record PlayerAccountView(string Id, string? Label, DateTimeOffset CreatedAt);
