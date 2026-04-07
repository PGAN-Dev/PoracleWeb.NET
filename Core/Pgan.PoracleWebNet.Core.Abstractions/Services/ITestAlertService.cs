namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ITestAlertService
{
    public Task SendTestAlertAsync(string userId, string alarmType, int uid);
}
