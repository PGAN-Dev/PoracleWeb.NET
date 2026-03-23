using Pgan.PoracleWebNet.Core.Abstractions.Repositories;

namespace Pgan.PoracleWebNet.Core.Abstractions.UnitsOfWork;

public interface IPoracleUnitOfWork : IDisposable
{
    public IMonsterRepository Monsters
    {
        get;
    }
    public IRaidRepository Raids
    {
        get;
    }
    public IEggRepository Eggs
    {
        get;
    }
    public IQuestRepository Quests
    {
        get;
    }
    public IInvasionRepository Invasions
    {
        get;
    }
    public ILureRepository Lures
    {
        get;
    }
    public INestRepository Nests
    {
        get;
    }
    public IGymRepository Gyms
    {
        get;
    }
    public IHumanRepository Humans
    {
        get;
    }
    public IProfileRepository Profiles
    {
        get;
    }
    public IPwebSettingRepository PwebSettings
    {
        get;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
