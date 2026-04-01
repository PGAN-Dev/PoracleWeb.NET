using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class HumanService(IHumanRepository repository) : IHumanService
{
    private readonly IHumanRepository _repository = repository;

    public async Task<IEnumerable<Human>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<Human?> GetByIdAsync(string id) => await this._repository.GetByIdAsync(id);

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo) => await this._repository.GetByIdAndProfileAsync(id, profileNo);

    public async Task<Human> CreateAsync(Human human) => await this._repository.CreateAsync(human);

    public async Task<Human> UpdateAsync(Human human) => await this._repository.UpdateAsync(human);

    public async Task<bool> ExistsAsync(string id) => await this._repository.ExistsAsync(id);

    // HACK: Direct DB cascade delete (ExecuteDeleteAsync on 8 alarm tables). PoracleNG has no
    // admin delete-all-alarms endpoint — would need to loop alarm types with bulk delete.
    // TODO: Migrate to PoracleNG API proxy once admin bulk-delete endpoint is available.
    // See: docs/poracleng-enhancement-requests.md#admin-delete-all-alarms
    public async Task<int> DeleteAllAlarmsByUserAsync(string userId) => await this._repository.DeleteAllAlarmsByUserAsync(userId);

    public async Task<bool> DeleteUserAsync(string userId) => await this._repository.DeleteUserAsync(userId);
}
