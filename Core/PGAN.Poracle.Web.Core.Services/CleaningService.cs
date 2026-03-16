using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Core.Services;

public class CleaningService : ICleaningService
{
    private readonly IMonsterService _monsterService;
    private readonly IRaidService _raidService;
    private readonly IEggService _eggService;
    private readonly IQuestService _questService;
    private readonly IInvasionService _invasionService;
    private readonly ILureService _lureService;
    private readonly INestService _nestService;
    private readonly IGymService _gymService;

    public CleaningService(
        IMonsterService monsterService,
        IRaidService raidService,
        IEggService eggService,
        IQuestService questService,
        IInvasionService invasionService,
        ILureService lureService,
        INestService nestService,
        IGymService gymService)
    {
        _monsterService = monsterService;
        _raidService = raidService;
        _eggService = eggService;
        _questService = questService;
        _invasionService = invasionService;
        _lureService = lureService;
        _nestService = nestService;
        _gymService = gymService;
    }

    public async Task<int> ToggleCleanMonstersAsync(string userId, int profileNo, int clean)
    {
        var items = await _monsterService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _monsterService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanRaidsAsync(string userId, int profileNo, int clean)
    {
        var items = await _raidService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _raidService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanEggsAsync(string userId, int profileNo, int clean)
    {
        var items = await _eggService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _eggService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanQuestsAsync(string userId, int profileNo, int clean)
    {
        var items = await _questService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _questService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanInvasionsAsync(string userId, int profileNo, int clean)
    {
        var items = await _invasionService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _invasionService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanLuresAsync(string userId, int profileNo, int clean)
    {
        var items = await _lureService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _lureService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanNestsAsync(string userId, int profileNo, int clean)
    {
        var items = await _nestService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _nestService.UpdateAsync(item);
            count++;
        }
        return count;
    }

    public async Task<int> ToggleCleanGymsAsync(string userId, int profileNo, int clean)
    {
        var items = await _gymService.GetByUserAsync(userId, profileNo);
        var count = 0;
        foreach (var item in items)
        {
            item.Clean = clean;
            await _gymService.UpdateAsync(item);
            count++;
        }
        return count;
    }
}
