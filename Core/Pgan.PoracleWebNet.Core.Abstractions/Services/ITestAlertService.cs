using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ITestAlertService
{
    Task SendTestAlertAsync(string userId, string alarmType, int uid);
}
