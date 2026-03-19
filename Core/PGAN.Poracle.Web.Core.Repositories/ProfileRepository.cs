using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

using Profile = PGAN.Poracle.Web.Core.Models.Profile;

namespace PGAN.Poracle.Web.Core.Repositories;

public class ProfileRepository(PoracleContext context, IMapper mapper) : IProfileRepository
{
    private readonly PoracleContext _context = context;
    private readonly IMapper _mapper = mapper;

    public async Task<IEnumerable<Profile>> GetByUserAsync(string userId)
    {
        var entities = await this._context.Profiles
            .Where(p => p.Id == userId)
            .ToListAsync();

        return this._mapper.Map<IEnumerable<Profile>>(entities);
    }

    public async Task<Profile?> GetByUserAndProfileNoAsync(string userId, int profileNo)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == userId && p.ProfileNo == profileNo);

        return entity is null ? null : this._mapper.Map<Profile>(entity);
    }

    public async Task<Profile> CreateAsync(Profile profile)
    {
        var entity = this._mapper.Map<ProfileEntity>(profile);
        this._context.Profiles.Add(entity);
        await this._context.SaveChangesAsync();
        return this._mapper.Map<Profile>(entity);
    }

    public async Task<Profile> UpdateAsync(Profile profile)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == profile.Id && p.ProfileNo == profile.ProfileNo)
            ?? throw new InvalidOperationException(
                $"Profile with id {profile.Id} and profileNo {profile.ProfileNo} not found.");

        this._mapper.Map(profile, entity);
        await this._context.SaveChangesAsync();
        return this._mapper.Map<Profile>(entity);
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
