using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface INestService : IAlarmService<Nest>, IBatchCreateService<Nest>
{
}
