using PGAN.Poracle.Web.Core.Abstractions.Repositories;

namespace PGAN.Poracle.Web.Core.Abstractions.UnitsOfWork;

public interface IPoracleUnitOfWork : IDisposable
{
    IMonsterRepository Monsters { get; }
    IRaidRepository Raids { get; }
    IEggRepository Eggs { get; }
    IQuestRepository Quests { get; }
    IInvasionRepository Invasions { get; }
    ILureRepository Lures { get; }
    INestRepository Nests { get; }
    IGymRepository Gyms { get; }
    IHumanRepository Humans { get; }
    IProfileRepository Profiles { get; }
    IPwebSettingRepository PwebSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
