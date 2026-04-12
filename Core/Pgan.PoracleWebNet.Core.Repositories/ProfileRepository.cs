using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

using Profile = Pgan.PoracleWebNet.Core.Models.Profile;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class ProfileRepository(PoracleContext context) : IProfileRepository
{
    private readonly PoracleContext _context = context;

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId)
    {
        var entities = await this._context.Profiles
            .Where(p => p.Id == userId)
            .ToListAsync();

        return entities.Select(e => e.ToModel());
    }

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == userId && p.ProfileNo == profileNo);

        return entity is null ? null : entity.ToModel();
    }

    public async Task<Profile> CreateAsync(Profile profile)
    {
        var entity = profile.ToEntity();
        this._context.Profiles.Add(entity);
        await this._context.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<Profile> UpdateAsync(Profile profile)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == profile.Id && p.ProfileNo == profile.ProfileNo)
            ?? throw new InvalidOperationException(
                $"Profile with id {profile.Id} and profileNo {profile.ProfileNo} not found.");

        profile.ApplyTo(entity);
        await this._context.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<bool> DeleteAsync(string userId, int profileNo)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == userId && p.ProfileNo == profileNo);

        if (entity is null)
        {
            return false;
        }

        this._context.Profiles.Remove(entity);
        await this._context.SaveChangesAsync();
        return true;
    }
}
