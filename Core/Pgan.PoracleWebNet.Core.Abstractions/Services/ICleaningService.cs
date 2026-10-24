using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface ICleaningService
{
    public Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo);

    /// <summary>Per alarm type, how many rules have auto-delete on and how many there are.</summary>
    public Task<Dictionary<string, CleanCount>> GetCleanCountsAsync(string userId, int profileNo);
    public Task<CleanToggleResult> ToggleCleanMonstersAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanRaidsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanEggsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanQuestsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanLuresAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanNestsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanGymsAsync(string userId, int profileNo, int clean);
    public Task<CleanToggleResult> ToggleCleanMaxBattlesAsync(string userId, int profileNo, int clean);
}
