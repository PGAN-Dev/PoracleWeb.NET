using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class QuickPickDefinitionRepository(PoracleWebContext context) : IQuickPickDefinitionRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<List<QuickPickDefinition>> GetAllGlobalAsync()
    {
        var entities = await this._context.QuickPickDefinitions
            .AsNoTracking()
            .Where(d => d.Scope == "global")
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToListAsync();

        return [.. entities.Select(e => e.ToModel())];
    }

    public async Task<List<QuickPickDefinition>> GetByOwnerAsync(string userId)
    {
        var entities = await this._context.QuickPickDefinitions
            .AsNoTracking()
            .Where(d => d.Scope == "user" && d.OwnerUserId == userId)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToListAsync();

        return [.. entities.Select(e => e.ToModel())];
    }

    public async Task<QuickPickDefinition?> GetByIdAsync(string id)
    {
        var entity = await this._context.QuickPickDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

        return entity is null ? null : entity.ToModel();
    }

    public async Task<QuickPickDefinition?> GetByIdAndOwnerAsync(string id, string userId)
    {
        var entity = await this._context.QuickPickDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.OwnerUserId == userId);

        return entity is null ? null : entity.ToModel();
    }

    public async Task CreateOrUpdateAsync(QuickPickDefinition definition)
    {
        var entity = await this._context.QuickPickDefinitions
            .FirstOrDefaultAsync(d => d.Id == definition.Id);

        if (entity is null)
        {
            entity = definition.ToEntity();
            this._context.QuickPickDefinitions.Add(entity);
        }
        else
        {
            definition.ApplyTo(entity);
        }

        await this._context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var entity = await this._context.QuickPickDefinitions
            .FirstOrDefaultAsync(d => d.Id == id);

        if (entity is not null)
        {
            this._context.QuickPickDefinitions.Remove(entity);
            await this._context.SaveChangesAsync();
        }
    }

    public async Task DeleteByIdAndOwnerAsync(string id, string userId)
    {
        var entity = await this._context.QuickPickDefinitions
            .FirstOrDefaultAsync(d => d.Id == id && d.OwnerUserId == userId);

        if (entity is not null)
        {
            this._context.QuickPickDefinitions.Remove(entity);
            await this._context.SaveChangesAsync();
        }
    }

    public async Task DeleteAllGlobalAsync()
    {
        var entities = await this._context.QuickPickDefinitions
            .Where(d => d.Scope == "global")
            .ToListAsync();

        if (entities.Count > 0)
        {
            this._context.QuickPickDefinitions.RemoveRange(entities);
            await this._context.SaveChangesAsync();
        }
    }

}
