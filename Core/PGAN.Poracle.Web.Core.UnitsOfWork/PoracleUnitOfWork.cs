using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;
using PGAN.Poracle.Web.Data;

namespace PGAN.Poracle.Web.Core.UnitsOfWork;

public class PoracleUnitOfWork : IPoracleUnitOfWork
{
    private readonly PoracleContext _context;
    private bool _disposed;

    public PoracleUnitOfWork(
        PoracleContext context,
        IMonsterRepository monsterRepository,
        IRaidRepository raidRepository,
        IEggRepository eggRepository,
        IQuestRepository questRepository,
        IInvasionRepository invasionRepository,
        ILureRepository lureRepository,
        INestRepository nestRepository,
        IGymRepository gymRepository,
        IHumanRepository humanRepository,
        IProfileRepository profileRepository,
        IPwebSettingRepository pwebSettingRepository)
    {
        _context = context;
        Monsters = monsterRepository;
        Raids = raidRepository;
        Eggs = eggRepository;
        Quests = questRepository;
        Invasions = invasionRepository;
        Lures = lureRepository;
        Nests = nestRepository;
        Gyms = gymRepository;
        Humans = humanRepository;
        Profiles = profileRepository;
        PwebSettings = pwebSettingRepository;
    }

    public IMonsterRepository Monsters { get; }
    public IRaidRepository Raids { get; }
    public IEggRepository Eggs { get; }
    public IQuestRepository Quests { get; }
    public IInvasionRepository Invasions { get; }
    public ILureRepository Lures { get; }
    public INestRepository Nests { get; }
    public IGymRepository Gyms { get; }
    public IHumanRepository Humans { get; }
    public IProfileRepository Profiles { get; }
    public IPwebSettingRepository PwebSettings { get; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _context.Dispose();
            }
            _disposed = true;
        }
    }
}
