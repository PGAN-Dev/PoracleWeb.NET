using System.Reflection;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class HumanRepository : IHumanRepository
{
    private readonly PoracleContext _context;
    private readonly IMapper _mapper;

    // Cached reflection results for EnsureNotNullDefaults
    private static readonly PropertyInfo[] WritableStringProperties =
        typeof(HumanEntity).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
            .ToArray();

    public HumanRepository(PoracleContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<IEnumerable<Human>> GetAllAsync()
    {
        var entities = await _context.Humans.ToListAsync();
        return _mapper.Map<IEnumerable<Human>>(entities);
    }

    public async Task<Human?> GetByIdAsync(string id)
    {
        var entity = await _context.Humans.FirstOrDefaultAsync(h => h.Id == id);
        return entity is null ? null : _mapper.Map<Human>(entity);
    }

    public async Task<Human?> GetByIdAndProfileAsync(string id, int profileNo)
    {
        var entity = await _context.Humans
            .FirstOrDefaultAsync(h => h.Id == id && h.CurrentProfileNo == profileNo);
        return entity is null ? null : _mapper.Map<Human>(entity);
    }

    public async Task<Human> CreateAsync(Human human)
    {
        var entity = _mapper.Map<HumanEntity>(human);
        EnsureNotNullDefaults(entity);
        _context.Humans.Add(entity);
        await _context.SaveChangesAsync();
        return _mapper.Map<Human>(entity);
    }

    public async Task<Human> UpdateAsync(Human human)
    {
        var entity = await _context.Humans.FirstOrDefaultAsync(h => h.Id == human.Id)
            ?? throw new InvalidOperationException($"Human with id {human.Id} not found.");

        _mapper.Map(human, entity);
        EnsureNotNullDefaults(entity);
        await _context.SaveChangesAsync();
        return _mapper.Map<Human>(entity);
    }

    public async Task<bool> ExistsAsync(string id)
    {
        return await _context.Humans.AnyAsync(h => h.Id == id);
    }

    public async Task<int> DeleteAllAlarmsByUserAsync(string userId)
    {
        var count = 0;
        count += await _context.Monsters.Where(m => m.Id == userId).ExecuteDeleteAsync();
        count += await _context.Raids.Where(r => r.Id == userId).ExecuteDeleteAsync();
        count += await _context.Eggs.Where(e => e.Id == userId).ExecuteDeleteAsync();
        count += await _context.Quests.Where(q => q.Id == userId).ExecuteDeleteAsync();
        count += await _context.Invasions.Where(i => i.Id == userId).ExecuteDeleteAsync();
        count += await _context.Lures.Where(l => l.Id == userId).ExecuteDeleteAsync();
        count += await _context.Nests.Where(n => n.Id == userId).ExecuteDeleteAsync();
        count += await _context.Gyms.Where(g => g.Id == userId).ExecuteDeleteAsync();
        return count;
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        var entity = await _context.Humans.FirstOrDefaultAsync(h => h.Id == userId);
        if (entity is null) return false;
        _context.Humans.Remove(entity);
        await _context.SaveChangesAsync();
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
