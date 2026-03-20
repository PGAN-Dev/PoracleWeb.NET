using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;

namespace PGAN.Poracle.Web.Core.Services;

public class CleaningService(IPoracleUnitOfWork unitOfWork) : ICleaningService
{
    private readonly IPoracleUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Dictionary<string, bool>> GetCleanStatusAsync(string userId, int profileNo)
    {
        var monsters = await this._unitOfWork.Monsters.GetByUserAsync(userId, profileNo);
        var raids = await this._unitOfWork.Raids.GetByUserAsync(userId, profileNo);
        var eggs = await this._unitOfWork.Eggs.GetByUserAsync(userId, profileNo);
        var quests = await this._unitOfWork.Quests.GetByUserAsync(userId, profileNo);
        var invasions = await this._unitOfWork.Invasions.GetByUserAsync(userId, profileNo);
        var lures = await this._unitOfWork.Lures.GetByUserAsync(userId, profileNo);
        var nests = await this._unitOfWork.Nests.GetByUserAsync(userId, profileNo);
        var gyms = await this._unitOfWork.Gyms.GetByUserAsync(userId, profileNo);

        return new Dictionary<string, bool>
        {
            ["monsters"] = monsters.Any() && monsters.All(m => m.Clean == 1),
            ["raids"] = raids.Any() && raids.All(r => r.Clean == 1),
            ["eggs"] = eggs.Any() && eggs.All(e => e.Clean == 1),
            ["quests"] = quests.Any() && quests.All(q => q.Clean == 1),
            ["invasions"] = invasions.Any() && invasions.All(i => i.Clean == 1),
            ["lures"] = lures.Any() && lures.All(l => l.Clean == 1),
            ["nests"] = nests.Any() && nests.All(n => n.Clean == 1),
            ["gyms"] = gyms.Any() && gyms.All(g => g.Clean == 1),
        };
    }

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Monsters.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Raids.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Eggs.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Quests.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Invasions.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Lures.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Nests.BulkUpdateCleanAsync(userId, profileNo, clean);

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean) => await this._unitOfWork.Gyms.BulkUpdateCleanAsync(userId, profileNo, clean);
}
