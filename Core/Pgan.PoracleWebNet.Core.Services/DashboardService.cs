using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class DashboardService(
    IMonsterRepository monsterRepository,
    IRaidRepository raidRepository,
    IEggRepository eggRepository,
    IQuestRepository questRepository,
    IInvasionRepository invasionRepository,
    ILureRepository lureRepository,
    INestRepository nestRepository,
    IGymRepository gymRepository) : IDashboardService
{
    private readonly IMonsterRepository _monsterRepository = monsterRepository;
    private readonly IRaidRepository _raidRepository = raidRepository;
    private readonly IEggRepository _eggRepository = eggRepository;
    private readonly IQuestRepository _questRepository = questRepository;
    private readonly IInvasionRepository _invasionRepository = invasionRepository;
    private readonly ILureRepository _lureRepository = lureRepository;
    private readonly INestRepository _nestRepository = nestRepository;
    private readonly IGymRepository _gymRepository = gymRepository;

    public async Task<DashboardCounts> GetCountsAsync(string userId, int profileNo) =>
        // Sequential to avoid DbContext concurrency issues (single scoped context)
        new()
        {
            Monsters = await this._monsterRepository.CountByUserAsync(userId, profileNo),
            Raids = await this._raidRepository.CountByUserAsync(userId, profileNo),
            Eggs = await this._eggRepository.CountByUserAsync(userId, profileNo),
            Quests = await this._questRepository.CountByUserAsync(userId, profileNo),
            Invasions = await this._invasionRepository.CountByUserAsync(userId, profileNo),
            Lures = await this._lureRepository.CountByUserAsync(userId, profileNo),
            Nests = await this._nestRepository.CountByUserAsync(userId, profileNo),
            Gyms = await this._gymRepository.CountByUserAsync(userId, profileNo)
        };
}
