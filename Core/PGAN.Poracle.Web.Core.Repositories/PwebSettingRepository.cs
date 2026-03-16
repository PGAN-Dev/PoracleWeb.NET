using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class PwebSettingRepository : IPwebSettingRepository
{
    private readonly PoracleContext _context;
    private readonly IMapper _mapper;

    public PwebSettingRepository(PoracleContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<IEnumerable<PwebSetting>> GetAllAsync()
    {
        var entities = await _context.PwebSettings.ToListAsync();
        return _mapper.Map<IEnumerable<PwebSetting>>(entities);
    }

    public async Task<PwebSetting?> GetByKeyAsync(string key)
    {
        var entity = await _context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == key);

        return entity is null ? null : _mapper.Map<PwebSetting>(entity);
    }

    public async Task<PwebSetting> CreateOrUpdateAsync(PwebSetting setting)
    {
        var entity = await _context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == setting.Setting);

        if (entity is null)
        {
            entity = _mapper.Map<PwebSettingEntity>(setting);
            _context.PwebSettings.Add(entity);
        }
        else
        {
            _mapper.Map(setting, entity);
        }

        await _context.SaveChangesAsync();
        return _mapper.Map<PwebSetting>(entity);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await _context.PwebSettings
            .FirstOrDefaultAsync(s => s.Setting == key);

        if (entity is null)
            return false;

        _context.PwebSettings.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }
}
