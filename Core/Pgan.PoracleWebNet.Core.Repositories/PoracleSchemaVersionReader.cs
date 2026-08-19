using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Core.Repositories;

/// <inheritdoc />
public partial class PoracleSchemaVersionReader(PoracleContext context, ILogger<PoracleSchemaVersionReader> logger)
    : IPoracleSchemaVersionReader
{
    private readonly PoracleContext _context = context;
    private readonly ILogger<PoracleSchemaVersionReader> _logger = logger;

    /// <inheritdoc />
    public async Task<long?> GetAppliedMigrationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // golang-migrate's table: one row, version plus a dirty flag. Identifiers stay unquoted so
            // the statement also parses on SQLite, which is what the repository tests run against.
            // Aliased to Value because that is the column name EF Core's scalar SqlQuery expects.
            var applied = await this._context.Database
                .SqlQueryRaw<long>("SELECT version AS Value FROM schema_migrations LIMIT 1")
                .FirstOrDefaultAsync(cancellationToken);

            return applied;
        }
        catch (Exception ex)
        {
            // A missing table is the ordinary case on a Poracle database that predates golang-migrate,
            // and a permission error is plausible on a locked-down deployment. Neither is worth an
            // exception the caller has to think about: the profile simply reports an unknown schema,
            // which unlocks nothing.
            LogSchemaUnreadable(this._logger, ex);
            return null;
        }
    }

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Debug,
        Message = "Could not read PoracleNG's applied migration; features gated on schema version stay off.")]
    private static partial void LogSchemaUnreadable(ILogger logger, Exception exception);
}
