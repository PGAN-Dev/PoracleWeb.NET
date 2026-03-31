using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class InvasionService(IInvasionRepository repository) : IInvasionService
{
    private readonly IInvasionRepository _repository = repository;

    public async Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo) => await this._repository.GetByUserAsync(userId, profileNo);

    public async Task<Invasion?> GetByUidAsync(int uid) => await this._repository.GetByUidAsync(uid);

    public async Task<Invasion> CreateAsync(string userId, Invasion model)
    {
        model.Id = userId;
        model.GruntType = NormalizeGruntType(model.GruntType);
        return await this._repository.CreateAsync(model);
    }

    public async Task<Invasion> UpdateAsync(Invasion model)
    {
        model.GruntType = NormalizeGruntType(model.GruntType);
        return await this._repository.UpdateAsync(model);
    }

    public async Task<bool> DeleteAsync(int uid) => await this._repository.DeleteAsync(uid);

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo) => await this._repository.DeleteAllByUserAsync(userId, profileNo);

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance) => await this._repository.UpdateDistanceByUserAsync(userId, profileNo, distance);

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance) => await this._repository.UpdateDistanceByUidsAsync(uids, userId, distance);

    public async Task<int> CountByUserAsync(string userId, int profileNo) => await this._repository.CountByUserAsync(userId, profileNo);

    public async Task<IEnumerable<Invasion>> BulkCreateAsync(string userId, IEnumerable<Invasion> models)
    {
        foreach (var model in models)
        {
            model.Id = userId;
            model.GruntType = NormalizeGruntType(model.GruntType);
        }

        return await this._repository.BulkCreateAsync(models);
    }

    private static string NormalizeGruntType(string? gruntType)
    {
        var normalized = gruntType?.ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) ? "everything" : normalized;
    }
}
