using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IDashboardService
{
    public Task<DashboardCounts> GetCountsAsync(string userId, int profileNo);
}
