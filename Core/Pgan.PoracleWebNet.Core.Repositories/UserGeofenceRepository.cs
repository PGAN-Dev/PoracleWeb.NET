using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class UserGeofenceRepository(PoracleWebContext context) : IUserGeofenceRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<List<UserGeofence>> GetByHumanIdAsync(string humanId)
    {
        var entities = await this._context.UserGeofences
            .AsNoTracking()
            .Where(g => g.HumanId == humanId)
            .OrderBy(g => g.GroupName)
            .ThenBy(g => g.DisplayName)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<UserGeofence?> GetByIdAsync(int id)
    {
        var entity = await this._context.UserGeofences
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);

        return entity is null ? null : entity.ToModel();
    }

    public async Task<UserGeofence?> GetByKojiNameAsync(string kojiName)
    {
        var entity = await this._context.UserGeofences
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.KojiName == kojiName);

        return entity is null ? null : entity.ToModel();
    }

    public async Task<int> GetCountByHumanIdAsync(string humanId) => await this._context.UserGeofences
            .AsNoTracking()
            .CountAsync(g => g.HumanId == humanId);

    public async Task<List<UserGeofence>> GetByStatusAsync(string status)
    {
        var entities = await this._context.UserGeofences
            .AsNoTracking()
            .Where(g => g.Status == status)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UserGeofence>> GetAllActiveAsync()
    {
        var entities = await this._context.UserGeofences
            .AsNoTracking()
            .Where(g => g.Status == "active" || g.Status == "pending_review")
            .OrderBy(g => g.KojiName)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UserGeofence>> GetAllAsync()
    {
        var entities = await this._context.UserGeofences
            .AsNoTracking()
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<UserGeofence> CreateAsync(UserGeofence geofence)
    {
        var entity = geofence.ToEntity();
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        this._context.UserGeofences.Add(entity);
        await this._context.SaveChangesAsync();

        return entity.ToModel();
    }

    public async Task<UserGeofence> UpdateAsync(UserGeofence geofence)
    {
        var entity = await this._context.UserGeofences
            .FirstOrDefaultAsync(g => g.Id == geofence.Id)
            ?? throw new InvalidOperationException($"UserGeofence with id {geofence.Id} not found.");

        geofence.ApplyTo(entity);
        entity.UpdatedAt = DateTime.UtcNow;

        await this._context.SaveChangesAsync();

        return entity.ToModel();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await this._context.UserGeofences
            .FirstOrDefaultAsync(g => g.Id == id)
            ?? throw new InvalidOperationException($"UserGeofence with id {id} not found.");

        this._context.UserGeofences.Remove(entity);
        await this._context.SaveChangesAsync();
    }
}
