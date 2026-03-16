using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;

namespace PGAN.Poracle.Web.Core.Services;

public class CleaningService : ICleaningService
{
    private readonly IPoracleUnitOfWork _unitOfWork;

    public CleaningService(IPoracleUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Monsters.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Raids.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Eggs.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Quests.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Invasions.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Lures.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Nests.BulkUpdateCleanAsync(userId, profileNo, clean);
    }

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean)
    {
        return await _unitOfWork.Gyms.BulkUpdateCleanAsync(userId, profileNo, clean);
    }
}
