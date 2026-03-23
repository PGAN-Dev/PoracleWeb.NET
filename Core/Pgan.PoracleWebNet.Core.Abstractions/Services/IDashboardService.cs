using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IDashboardService
{
    public Task<DashboardCounts> GetCountsAsync(string userId, int profileNo);
}
