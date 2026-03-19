using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IScannerService
{
    public Task<IEnumerable<QuestData>> GetActiveQuestsAsync();
    public Task<IEnumerable<RaidData>> GetActiveRaidsAsync();
}
