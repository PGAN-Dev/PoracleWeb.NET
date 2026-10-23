namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ISettingsMigrationService
{
    /// <summary>
    /// Ensures default site settings exist and repairs stored ones. Does not overwrite existing values,
    /// and reads nothing from Poracle's database. Called on every startup.
    /// </summary>
    Task SeedDefaultsAsync();
}
