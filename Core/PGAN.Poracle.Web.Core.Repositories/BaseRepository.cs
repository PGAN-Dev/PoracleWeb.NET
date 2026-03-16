using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Data;

namespace PGAN.Poracle.Web.Core.Repositories;

public abstract class BaseRepository<TEntity, TModel> : IBaseRepository<TModel>
    where TEntity : class
    where TModel : class
{
    protected readonly PoracleContext Context;
    protected readonly IMapper Mapper;

    protected BaseRepository(PoracleContext context, IMapper mapper)
    {
        Context = context;
        Mapper = mapper;
    }

    protected abstract DbSet<TEntity> DbSet { get; }

    // Subclasses build a Where expression for userId + profileNo filtering
    protected abstract Expression<Func<TEntity, bool>> UserProfileFilter(string userId, int profileNo);

    // Subclasses build a Where expression for uid filtering
    protected abstract Expression<Func<TEntity, bool>> UidFilter(int uid);

    // Subclasses build a Where expression for userId-only filtering
    protected abstract Expression<Func<TEntity, bool>> UserFilter(string userId);

    public async Task<IEnumerable<TModel>> GetByUserAsync(string userId, int profileNo)
    {
        var entities = await DbSet
            .Where(UserProfileFilter(userId, profileNo))
            .ToListAsync();

        return Mapper.Map<IEnumerable<TModel>>(entities);
    }

    public async Task<TModel?> GetByUidAsync(int uid)
    {
        var entity = await DbSet
            .FirstOrDefaultAsync(UidFilter(uid));

        return entity is null ? null : Mapper.Map<TModel>(entity);
    }

    public async Task<TModel> CreateAsync(TModel model)
    {
        var entity = Mapper.Map<TEntity>(model);
        // Ensure NOT NULL text fields have defaults
        EnsureNotNullDefaults(entity);
        DbSet.Add(entity);
        await Context.SaveChangesAsync();
        return Mapper.Map<TModel>(entity);
    }

    private static void EnsureNotNullDefaults(TEntity entity)
    {
        foreach (var prop in typeof(TEntity).GetProperties())
        {
            if (prop.PropertyType == typeof(string) && prop.CanWrite)
            {
                if (prop.GetValue(entity) == null)
                {
                    prop.SetValue(entity, string.Empty);
                }
            }
        }
    }

    public async Task<TModel> UpdateAsync(TModel model)
    {
        var modelUid = GetUidFromModel(model);

        var entity = await DbSet.FirstOrDefaultAsync(UidFilter(modelUid))
            ?? throw new InvalidOperationException($"Entity with uid {modelUid} not found.");

        Mapper.Map(model, entity);
        EnsureNotNullDefaults(entity);
        await Context.SaveChangesAsync();
        return Mapper.Map<TModel>(entity);
    }

    public async Task<bool> DeleteAsync(int uid)
    {
        var entity = await DbSet.FirstOrDefaultAsync(UidFilter(uid));
        if (entity is null)
            return false;

        DbSet.Remove(entity);
        await Context.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var entities = await DbSet
            .Where(UserProfileFilter(userId, profileNo))
            .ToListAsync();

        DbSet.RemoveRange(entities);
        await Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var entities = await DbSet
            .Where(UserProfileFilter(userId, profileNo))
            .ToListAsync();

        foreach (var entity in entities)
        {
            SetDistance(entity, distance);
        }

        await Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        return await DbSet
            .CountAsync(UserProfileFilter(userId, profileNo));
    }

    private int GetUidFromModel(TModel model)
    {
        var property = typeof(TModel).GetProperty("Uid")
            ?? throw new InvalidOperationException($"Model type {typeof(TModel).Name} does not have a Uid property.");
        return (int)(property.GetValue(model) ?? 0);
    }

    private static void SetDistance(TEntity entity, int distance)
    {
        var property = typeof(TEntity).GetProperty("Distance");
        property?.SetValue(entity, distance);
    }
}
