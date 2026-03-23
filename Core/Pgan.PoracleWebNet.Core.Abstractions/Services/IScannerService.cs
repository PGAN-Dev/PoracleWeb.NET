using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IScannerService
{
    public Task<IEnumerable<QuestData>> GetActiveQuestsAsync();
    public Task<IEnumerable<RaidData>> GetActiveRaidsAsync();
}
