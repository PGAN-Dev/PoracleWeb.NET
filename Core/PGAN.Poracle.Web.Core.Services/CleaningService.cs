using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;

namespace PGAN.Poracle.Web.Core.Services;

public class CleaningService(IPoracleUnitOfWork unitOfWork) : ICleaningService
{
    private readonly IPoracleUnitOfWork _unitOfWork = unitOfWork;

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Monsters.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Raids.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Eggs.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Quests.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Invasions.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Lures.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Nests.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Gyms.BulkUpdateCleanAsync(userId, profileNo, clean);
}
