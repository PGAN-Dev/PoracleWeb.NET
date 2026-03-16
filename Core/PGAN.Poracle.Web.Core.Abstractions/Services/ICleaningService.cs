namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface ICleaningService
{
    Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean);
    Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean);
}
