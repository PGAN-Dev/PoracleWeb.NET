namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ICleaningService
{
    public Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo);
    public Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean);
    public Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean);
}
