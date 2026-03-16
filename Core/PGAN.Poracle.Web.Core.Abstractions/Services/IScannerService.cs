using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IScannerService
{
    Task<IEnumerable<QuestData>> GetActiveQuestsAsync();
    Task<IEnumerable<RaidData>> GetActiveRaidsAsync();
}
