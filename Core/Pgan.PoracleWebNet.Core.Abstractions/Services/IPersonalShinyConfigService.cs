using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPersonalShinyConfigService
{
    /// <summary>
    /// The rate-feed and self-service settings read from PoracleNG's config. Any fault or absent
    /// key reads as <c>(false, false)</c>.
    /// </summary>
    Task<PersonalShinyConfig> GetAsync();
}
