using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class ProfileService(IProfileRepository repository) : IProfileService
{
    private readonly IProfileRepository _repository = repository;

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId) => await this._repository.GetByUserAsync(userId);

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo) => await this._repository.GetByUserAndProfileNoAsync(userId, profileNo);

    public async Task<Profile> CreateAsync(Profile profile) => await this._repository.CreateAsync(profile);

    public async Task<Profile> UpdateAsync(Profile profile) => await this._repository.UpdateAsync(profile);

    public async Task<bool> DeleteAsync(string userId, int profileNo) => await this._repository.DeleteAsync(userId, profileNo);
}
