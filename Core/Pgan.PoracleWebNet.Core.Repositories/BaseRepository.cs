using System.Linq.Expressions;
using System.Reflection;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Core.Repositories;

public abstract class BaseRepository<TEntity, TModel>(PoracleContext context, IMapper mapper) : IBaseRepository<TModel>
    where TEntity : class
    where TModel : class
{
    protected readonly PoracleContext Context = context;
    protected readonly IMapper Mapper = mapper;

    // Cached reflection results for EnsureNotNullDefaults — only non-nullable string properties.
    // Nullable string properties (string?) like GymId and Template must remain NULL because
    // PoracleNG/PoracleJS use NULL vs empty-string semantics for matching (e.g. gym_id IS NULL
    // means "general alarm", while gym_id = '' would be treated as a specific gym filter).
    private static readonly PropertyInfo[] WritableNonNullableStringProperties = GetNonNullableStringProperties();

    // Cached Uid property for GetUidFromModel
    private static readonly PropertyInfo? UidProperty =
        typeof(TModel).GetProperty("Uid");

    // Cached Distance property for SetDistance
    private static readonly PropertyInfo? DistanceProperty =
        typeof(TEntity).GetProperty("Distance");

    // Cached Clean property for SetClean
    private static readonly PropertyInfo? CleanProperty =
        typeof(TEntity).GetProperty("Clean");

    protected abstract DbSet<TEntity> DbSet
    {
        get;
    }

    // Subclasses build a Where expression for userId + profileNo filtering
    protected abstract Expression<Func<TEntity, bool>> UserProfileFilter(string userId, int profileNo);

    // Subclasses build a Where expression for uid filtering
    protected abstract Expression<Func<TEntity, bool>> UidFilter(int uid);

    // Subclasses build a Where expression for userId-only filtering
    protected abstract Expression<Func<TEntity, bool>> UserFilter(string userId);

    public async Task<IEnumerable<TModel>> GetByUserAsync(string userId, int profileNo)
    {
        var entities = await this.DbSet
            .AsNoTracking()
            .Where(this.UserProfileFilter(userId, profileNo))
            .ToListAsync();

        return this.Mapper.Map<IEnumerable<TModel>>(entities);
    }

    public async Task<TModel?> GetByUidAsync(int uid)
    {
        var entity = await this.DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(this.UidFilter(uid));

        return entity is null ? null : this.Mapper.Map<TModel>(entity);
    }

    public async Task<TModel> CreateAsync(TModel model)
    {
        var entity = this.Mapper.Map<TEntity>(model);
        // Ensure NOT NULL text fields have defaults
        EnsureNotNullDefaults(entity);
        this.DbSet.Add(entity);
        await this.Context.SaveChangesAsync();
        return this.Mapper.Map<TModel>(entity);
    }

    private static PropertyInfo[] GetNonNullableStringProperties()
    {
        var nullabilityContext = new NullabilityInfoContext();
        return
        [
            .. typeof(TEntity).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
                .Where(p => nullabilityContext.Create(p).WriteState != NullabilityState.Nullable),
        ];
    }

    private static void EnsureNotNullDefaults(TEntity entity)
    {
        foreach (var prop in WritableNonNullableStringProperties)
        {
            if (prop.GetValue(entity) == null)
            {
                prop.SetValue(entity, string.Empty);
            }
        }
    }

    public async Task<TModel> UpdateAsync(TModel model)
    {
        var modelUid = BaseRepository<TEntity, TModel>.GetUidFromModel(model);

        var entity = await this.DbSet.FirstOrDefaultAsync(this.UidFilter(modelUid))
            ?? throw new InvalidOperationException($"Entity with uid {modelUid} not found.");

        this.Mapper.Map(model, entity);
        EnsureNotNullDefaults(entity);
        await this.Context.SaveChangesAsync();
        return this.Mapper.Map<TModel>(entity);
    }

    public async Task<bool> DeleteAsync(int uid)
    {
        var entity = await this.DbSet.FirstOrDefaultAsync(this.UidFilter(uid));
        if (entity is null)
        {
            return false;
        }

        this.DbSet.Remove(entity);
        await this.Context.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var entities = await this.DbSet
            .Where(this.UserProfileFilter(userId, profileNo))
            .ToListAsync();

        this.DbSet.RemoveRange(entities);
        await this.Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<int> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var entities = await this.DbSet
            .Where(this.UserProfileFilter(userId, profileNo))
            .ToListAsync();

        foreach (var entity in entities)
        {
            SetDistance(entity, distance);
        }

        await this.Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<int> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var entities = await this.DbSet
            .Where(this.UserFilter(userId))
            .Where(e => uids.Contains(EF.Property<int>(e, "Uid")))
            .ToListAsync();

        foreach (var entity in entities)
        {
            SetDistance(entity, distance);
        }

        await this.Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<int> BulkUpdateCleanAsync(string userId, int profileNo, int clean)
    {
        var entities = await this.DbSet
            .Where(this.UserProfileFilter(userId, profileNo))
            .ToListAsync();

        foreach (var entity in entities)
        {
            SetClean(entity, clean);
        }

        await this.Context.SaveChangesAsync();
        return entities.Count;
    }

    public async Task<IEnumerable<TModel>> BulkCreateAsync(IEnumerable<TModel> models)
    {
        var entities = models.Select(m =>
        {
            var entity = this.Mapper.Map<TEntity>(m);
            EnsureNotNullDefaults(entity);
            return entity;
        }).ToList();

        this.DbSet.AddRange(entities);
        await this.Context.SaveChangesAsync();
        return this.Mapper.Map<IEnumerable<TModel>>(entities);
    }

    public async Task<int> CountByUserAsync(string userId, int profileNo) => await this.DbSet
            .CountAsync(this.UserProfileFilter(userId, profileNo));

    private static int GetUidFromModel(TModel model)
    {
        var property = UidProperty
            ?? throw new InvalidOperationException($"Model type {typeof(TModel).Name} does not have a Uid property.");
        return (int)(property.GetValue(model) ?? 0);
    }

    private static void SetDistance(TEntity entity, int distance) => DistanceProperty?.SetValue(entity, distance);

    private static void SetClean(TEntity entity, int clean) => CleanProperty?.SetValue(entity, clean);
}
