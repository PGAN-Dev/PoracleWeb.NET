using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// One request for many alarms of a type, as the add dialogs' multi-select sends them: one dry run and one
/// commit to PoracleNG instead of a create per row.
/// </summary>
public interface IBatchCreateService<TModel>
{
    public Task<BatchCreateResult<TModel>> CreateBatchAsync(string userId, IReadOnlyList<TModel> models);
}
