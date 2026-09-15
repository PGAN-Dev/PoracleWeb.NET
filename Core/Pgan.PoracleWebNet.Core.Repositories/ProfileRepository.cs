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

    public async Task<bool> RenameAsync(string userId, int profileNo, string name)
    {
        var entity = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == userId && p.ProfileNo == profileNo);

        if (entity is null)
        {
            return false;
        }

        entity.Name = name;
        await this._context.SaveChangesAsync();
        return true;
    }
}
