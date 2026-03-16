using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IQuestService
{
    Task<IEnumerable<Quest>> GetByUserAsync(string userId, int profileNo);
    Task<Quest?> GetByUidAsync(int uid);
    Task<Quest> CreateAsync(string userId, Quest model);
    Task<Quest> UpdateAsync(Quest model);
    Task<bool> DeleteAsync(int uid);
    Task<int> DeleteAllByUserAsync(string userId, int profileNo);
    Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance);
    Task<int> CountByUserAsync(string userId, int profileNo);
}
