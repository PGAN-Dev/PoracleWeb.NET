using System.Reflection;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class HumanRepository(PoracleContext context, IMapper mapper) : IHumanRepository
{
    private readonly PoracleContext _context = context;
    private readonly IMapper _mapper = mapper;

    // Cached reflection results for EnsureNotNullDefaults
    private static readonly PropertyInfo[] WritableStringProperties =
        [.. typeof(HumanEntity).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite)];

    public async Task<IEnumerable<Human>> GetAllAsync()
    {
        var entities = await this._context.Humans.ToListAsync();
        return this._mapper.Map<IEnumerable<Human>>(entities);
    }

    public async Task<Human?> GetByIdAsync(string id)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == id);
        return entity is null ? null : this._mapper.Map<Human>(entity);
    }

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo)
    {
        var entity = await this._context.Humans
            .FirstOrDefaultAsync(h => h.Id == id && h.CurrentProfileNo == profileNo);
        return entity is null ? null : this._mapper.Map<Human>(entity);
    }

    public async Task<Human> CreateAsync(Human human)
    {
        var entity = this._mapper.Map<HumanEntity>(human);
        EnsureNotNullDefaults(entity);
        this._context.Humans.Add(entity);
        await this._context.SaveChangesAsync();
        return this._mapper.Map<Human>(entity);
    }

    public async Task<Human> UpdateAsync(Human human)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == human.Id)
            ?? throw new InvalidOperationException($"Human with id {human.Id} not found.");

        this._mapper.Map(human, entity);
        EnsureNotNullDefaults(entity);
        await this._context.SaveChangesAsync();
        return this._mapper.Map<Human>(entity);
    }

    public async Task<bool> ExistsAsync(string id) => await this._context.Humans.AnyAsync(h => h.Id == id);

    public async Task<int> DeleteAllAlarmsByUserAsync(string userId)
    {
        var count = 0;
        count += await this._context.Monsters.Where(m => m.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Raids.Where(r => r.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Eggs.Where(e => e.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Quests.Where(q => q.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Invasions.Where(i => i.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Lures.Where(l => l.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Nests.Where(n => n.Id == userId).ExecuteDeleteAsync();
        count += await this._context.Gyms.Where(g => g.Id == userId).ExecuteDeleteAsync();
        return count;
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        var entity = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == userId);
        if (entity is null)
        {
            return false;
        }

        this._context.Humans.Remove(entity);
        await this._context.SaveChangesAsync();
        return true;
    }

    private static void EnsureNotNullDefaults(HumanEntity entity)
    {
        foreach (var prop in WritableStringProperties)
        {
            if (prop.GetValue(entity) == null)
            {
                prop.SetValue(entity, string.Empty);
            }
        }
    }
}
