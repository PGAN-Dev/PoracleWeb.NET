using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class SiteSettingRepository(PoracleWebContext context) : ISiteSettingRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<IEnumerable<SiteSetting>> GetAllAsync()
    {
        var entities = await this._context.SiteSettings
            .AsNoTracking()
            .ToListAsync();

        return entities.Select(e => e.ToModel());
    }

    public async Task<IEnumerable<SiteSetting>> GetByCategoryAsync(string category)
    {
        var entities = await this._context.SiteSettings
            .AsNoTracking()
            .Where(s => s.Category == category)
            .ToListAsync();

        return entities.Select(e => e.ToModel());
    }

    public async Task<SiteSetting?> GetByKeyAsync(string key)
    {
        var entity = await this._context.SiteSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        return entity is null ? null : entity.ToModel();
    }

    public async Task<SiteSetting> CreateOrUpdateAsync(SiteSetting setting)
    {
        var entity = await this._context.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == setting.Key);

        if (entity is null)
        {
            entity = setting.ToEntity();
            this._context.SiteSettings.Add(entity);
        }
        else
        {
            entity.Value = setting.Value;
            entity.ValueType = setting.ValueType;
            entity.Category = setting.Category;
        }

        await this._context.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await this._context.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == key);

        if (entity is null)
        {
            return false;
        }

        this._context.SiteSettings.Remove(entity);
        await this._context.SaveChangesAsync();
        return true;
    }
}
