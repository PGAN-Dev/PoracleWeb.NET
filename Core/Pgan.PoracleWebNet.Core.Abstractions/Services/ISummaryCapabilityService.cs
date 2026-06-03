namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ISummaryCapabilityService
{
    Task<bool> IsQuestSummaryEnabledAsync();
}
