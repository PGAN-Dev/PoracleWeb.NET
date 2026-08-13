using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class HumanRepository(PoracleContext context) : IHumanRepository
{
    private readonly PoracleContext _context = context;

    // Cached reflection results for EnsureNotNullDefaults
    private static readonly PropertyInfo[] WritableStringProperties =
        [.. typeof(HumanEntity).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite)];

    public async Task<IEnumerable<Human>> GetAllAsync()
    {
        var entities = await this._context.Humans.ToListAsync();
        return entities.Select(e => e.ToModel());
    }

    public async Task<Human?> GetByIdAsync(string id)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == id);
        return entity is null ? null : entity.ToModel();
    }

    public async Task<IEnumerable<Human>> GetByIdsAsync(IEnumerable<string> ids)
    {
        var idArray = ids.ToArray();
        if (idArray.Length == 0)
        {
            return [];
        }

        // MySql.EntityFrameworkCore doesn't support List<T>.Contains() in LINQ.
        // Fetch individually since the ID list is small (distinct geofence owners).
        var results = new List<HumanEntity>();
        foreach (var id in idArray)
        {
            var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == id);
            if (entity != null)
            {
                results.Add(entity);
            }
        }

        return results.Select(e => e.ToModel());
    }

    public async Task<Human> CreateAsync(Human human)
    {
        var entity = human.ToEntity();
        EnsureNotNullDefaults(entity);
        this._context.Humans.Add(entity);
        await this._context.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<Human> UpdateAsync(Human human)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == human.Id)
            ?? throw new InvalidOperationException($"Human with id {human.Id} not found.");

        human.ApplyTo(entity);
        EnsureNotNullDefaults(entity);
        await this._context.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<bool> ExistsAsync(string id) => await this._context.Humans.AnyAsync(h => h.Id == id);

    // DeleteAllAlarmsByUserAsync lived here and was dead: HumanService has looped the tracking proxy
    // since the PoracleNG migration, so nothing reached it. Its eight ExecuteDeleteAsync calls would
    // each have emitted the aliased DELETE that MariaDB rejects (#707), so it could not have worked
    // had anything called it. Alarm deletion belongs to PoracleNG, which reloads its own state.

    public async Task<bool> DeleteUserAsync(string userId)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == userId);
        if (entity is null)
        {
            return false;
        }

        // The profiles rows outlived the human: invisible to every API surface, but re-creating the same
        // id adopted them verbatim -- old areas, old coordinates, old active_hours -- and PoracleNG's
        // human-create then collided on the surviving (id, profile_no) and errored after committing the
        // human (#482). Removed in the same SaveChangesAsync so the two cannot part company. See #481.
        this._context.Humans.Remove(entity);
        this._context.Profiles.RemoveRange(this._context.Profiles.Where(p => p.Id == userId));
        await this._context.SaveChangesAsync();
        return true;
    }

    private static void EnsureNotNullDefaults(HumanEntity entity)
    {
        foreach (var prop in WritableStringProperties.Where(prop => prop.GetValue(entity) == null))
        {
            prop.SetValue(entity, string.Empty);
        }
    }
}
