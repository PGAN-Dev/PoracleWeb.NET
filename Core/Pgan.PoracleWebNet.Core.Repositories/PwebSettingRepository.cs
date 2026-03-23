using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class PwebSettingRepository(PoracleContext context, IMapper mapper) : IPwebSettingRepository
{
    private readonly PoracleContext _context = context;
    private readonly IMapper _mapper = mapper;

    public async Task<IEnumerable<PwebSetting>> GetAllAsync()
    {
        var entities = await this._context.PwebSettings.ToListAsync();
        return this._mapper.Map<IEnumerable<PwebSetting>>(entities);
    }

    public async Task<PwebSetting?> GetByKeyAsync(string key)
    {
        var entity = await this._context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == key);

        return entity is null ? null : this._mapper.Map<PwebSetting>(entity);
    }

    public async Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting)
    {
        var entity = await this._context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == setting.Setting);

        if (entity is null)
        {
            entity = this._mapper.Map<PwebSettingEntity>(setting);
            this._context.PwebSettings.Add(entity);
        }
        else
        {
            this._mapper.Map(setting, entity);
        }

        await this._context.SaveChangesAsync();
        return this._mapper.Map<PwebSetting>(entity);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await this._context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == key);

        if (entity is null)
        {
            return false;
        }

        this._context.PwebSettings.Remove(entity);
        await this._context.SaveChangesAsync();
        return true;
    }
}
