using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class UserGeofenceRepository(PoracleContext context, IMapper mapper) : IUserGeofenceRepository
{
    private readonly PoracleContext _context = context;
    private readonly IMapper _mapper = mapper;

    public async Task<List<UserGeofence>> GetByHumanIdAsync(string humanId, int profileNo)
    {
        var entities = await this._context.UserGeofences
            .AsNoTracking()
            .Where(g => g.HumanId == humanId && g.ProfileNo == profileNo)
            .ToListAsync();

        return this._mapper.Map<List<UserGeofence>>(entities);
    }

    public async Task<UserGeofence?> GetByIdAsync(int id)
    {
        var entity = await this._context.UserGeofences
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);

        return entity is null ? null : this._mapper.Map<UserGeofence>(entity);
    }

    public async Task<int> GetCountByHumanIdAsync(string humanId)
    {
        return await this._context.UserGeofences
            .CountAsync(g => g.HumanId == humanId);
    }

    public async Task<UserGeofence> CreateAsync(UserGeofence geofence)
    {
        var entity = this._mapper.Map<UserGeofenceEntity>(geofence);
        this._context.UserGeofences.Add(entity);
        await this._context.SaveChangesAsync();
        return this._mapper.Map<UserGeofence>(entity);
    }

    public async Task<UserGeofence> UpdateAsync(UserGeofence geofence)
    {
        var entity = await this._context.UserGeofences
            .FirstOrDefaultAsync(g => g.Id == geofence.Id)
            ?? throw new InvalidOperationException($"UserGeofence with id {geofence.Id} not found.");

        this._mapper.Map(geofence, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        await this._context.SaveChangesAsync();
        return this._mapper.Map<UserGeofence>(entity);
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await this._context.UserGeofences
            .FirstOrDefaultAsync(g => g.Id == id);

        if (entity is not null)
        {
            this._context.UserGeofences.Remove(entity);
            await this._context.SaveChangesAsync();
        }
    }
}
