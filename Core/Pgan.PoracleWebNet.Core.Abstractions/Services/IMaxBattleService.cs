using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IMaxBattleService : IAlarmService<MaxBattle>, IBatchCreateService<MaxBattle>
{
}
