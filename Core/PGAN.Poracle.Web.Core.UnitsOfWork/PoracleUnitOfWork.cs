using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;
using PGAN.Poracle.Web.Data;

namespace PGAN.Poracle.Web.Core.UnitsOfWork;

public class PoracleUnitOfWork(
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
    IPwebSettingRepository pwebSettingRepository) : IPoracleUnitOfWork
{
    private readonly PoracleContext _context = context;
    private bool _disposed;

    public IMonsterRepository Monsters
    {
        get;
    } = monsterRepository;
    public IRaidRepository Raids
    {
        get;
    } = raidRepository;
    public IEggRepository Eggs
    {
        get;
    } = eggRepository;
    public IQuestRepository Quests
    {
        get;
    } = questRepository;
    public IInvasionRepository Invasions
    {
        get;
    } = invasionRepository;
    public ILureRepository Lures
    {
        get;
    } = lureRepository;
    public INestRepository Nests
    {
        get;
    } = nestRepository;
    public IGymRepository Gyms
    {
        get;
    } = gymRepository;
    public IHumanRepository Humans
    {
        get;
    } = humanRepository;
    public IProfileRepository Profiles
    {
        get;
    } = profileRepository;
    public IPwebSettingRepository PwebSettings
    {
        get;
    } = pwebSettingRepository;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => await this._context.SaveChangesAsync(cancellationToken);

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            if (disposing)
            {
                this._context.Dispose();
            }
            this._disposed = true;
        }
    }
}
