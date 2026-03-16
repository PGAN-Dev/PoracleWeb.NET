using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class DashboardService : IDashboardService
{
    private readonly IMonsterRepository _monsterRepository;
    private readonly IRaidRepository _raidRepository;
    private readonly IEggRepository _eggRepository;
    private readonly IQuestRepository _questRepository;
    private readonly IInvasionRepository _invasionRepository;
    private readonly ILureRepository _lureRepository;
    private readonly INestRepository _nestRepository;
    private readonly IGymRepository _gymRepository;

    public DashboardService(
        IMonsterRepository monsterRepository,
        IRaidRepository raidRepository,
        IEggRepository eggRepository,
        IQuestRepository questRepository,
        IInvasionRepository invasionRepository,
        ILureRepository lureRepository,
        INestRepository nestRepository,
        IGymRepository gymRepository)
    {
        _monsterRepository = monsterRepository;
        _raidRepository = raidRepository;
        _eggRepository = eggRepository;
        _questRepository = questRepository;
        _invasionRepository = invasionRepository;
        _lureRepository = lureRepository;
        _nestRepository = nestRepository;
        _gymRepository = gymRepository;
    }

    public async Task<DashboardCounts> GetCountsAsync(string userId, int profileNo)
    {
        // Sequential to avoid DbContext concurrency issues (single scoped context)
        return new DashboardCounts
        {
            Monsters = await _monsterRepository.CountByUserAsync(userId, profileNo),
            Raids = await _raidRepository.CountByUserAsync(userId, profileNo),
            Eggs = await _eggRepository.CountByUserAsync(userId, profileNo),
            Quests = await _questRepository.CountByUserAsync(userId, profileNo),
            Invasions = await _invasionRepository.CountByUserAsync(userId, profileNo),
            Lures = await _lureRepository.CountByUserAsync(userId, profileNo),
            Nests = await _nestRepository.CountByUserAsync(userId, profileNo),
            Gyms = await _gymRepository.CountByUserAsync(userId, profileNo)
        };
    }
}
