using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class ProfileService : IProfileService
{
    private readonly IProfileRepository _repository;

    public ProfileService(IProfileRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId)
    {
        return await _repository.GetByUserAsync(userId);
    }

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo)
    {
        return await _repository.GetByUserAndProfileNoAsync(userId, profileNo);
    }

    public async Task<Profile> CreateAsync(Profile profile)
    {
        return await _repository.CreateAsync(profile);
    }

    public async Task<Profile> UpdateAsync(Profile profile)
    {
        return await _repository.UpdateAsync(profile);
    }

    public async Task<bool> DeleteAsync(string userId, int profileNo)
    {
        return await _repository.DeleteAsync(userId, profileNo);
    }
}
