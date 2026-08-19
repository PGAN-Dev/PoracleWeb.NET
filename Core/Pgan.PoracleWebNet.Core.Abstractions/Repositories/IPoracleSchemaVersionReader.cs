namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

/// <summary>
/// Reads the migration number PoracleNG has applied to its own database.
/// </summary>
/// <remarks>
/// PoracleNG's <c>/health</c> capability map describes bot and template-editor features; nothing in it
/// says which alarm columns exist. The applied migration does, and PoracleNG runs its migrations at
/// startup, so the number tracks the binary. Reading it is a stopgap until upstream publishes it —
/// see the note on <see cref="Models.PoracleServerProfile.SchemaVersion"/>.
/// </remarks>
public interface IPoracleSchemaVersionReader
{
    /// <summary>
    /// The applied migration number, or null when it cannot be read — no table, no permission, no
    /// database. Never throws: an unknown schema is a valid answer that simply unlocks nothing.
    /// </summary>
    Task<long?> GetAppliedMigrationAsync(CancellationToken cancellationToken = default);
}
