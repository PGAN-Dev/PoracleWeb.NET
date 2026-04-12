namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ISettingsMigrationService
{
    Task MigrateAsync();

    /// <summary>
    /// Ensures default site settings exist. Does not overwrite existing values.
    /// Called on every startup (after migration) so fresh installs get sensible defaults.
    /// </summary>
    Task SeedDefaultsAsync();
}
