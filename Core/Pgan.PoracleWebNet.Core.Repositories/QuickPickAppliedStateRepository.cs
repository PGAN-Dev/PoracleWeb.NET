using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class QuickPickAppliedStateRepository(PoracleWebContext context) : IQuickPickAppliedStateRepository
{
    private readonly PoracleWebContext _context = context;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<QuickPickAppliedState?> GetAsync(string userId, int profileNo, string quickPickId)
    {
        var entity = await this._context.QuickPickAppliedStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ProfileNo == profileNo && s.QuickPickId == quickPickId);

        return entity is null ? null : MapToModel(entity);
    }

    public async Task<List<QuickPickAppliedState>> GetByUserAndProfileAsync(string userId, int profileNo)
    {
        var entities = await this._context.QuickPickAppliedStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.ProfileNo == profileNo)
            .ToListAsync();

        return [.. entities.Select(MapToModel)];
    }

    public async Task<List<QuickPickAppliedState>> GetByUserAsync(string userId)
    {
        var entities = await this._context.QuickPickAppliedStates
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync();

        return [.. entities.Select(MapToModel)];
    }

    public async Task CreateOrUpdateAsync(QuickPickAppliedState state)
    {
        var entity = await this._context.QuickPickAppliedStates
            .FirstOrDefaultAsync(s => s.UserId == state.UserId && s.ProfileNo == state.ProfileNo && s.QuickPickId == state.QuickPickId);

        if (entity is null)
        {
            entity = new QuickPickAppliedStateEntity
            {
                UserId = state.UserId,
                ProfileNo = state.ProfileNo,
                QuickPickId = state.QuickPickId,
                AlarmType = state.AlarmType,
                AppliedAt = DateTime.UtcNow,
                ExcludePokemonIdsJson = JsonSerializer.Serialize(state.ExcludePokemonIds, JsonOptions),
                TrackedUidsJson = JsonSerializer.Serialize(state.TrackedUids, JsonOptions),
            };
            this._context.QuickPickAppliedStates.Add(entity);
        }
        else
        {
            entity.AppliedAt = DateTime.UtcNow;
            entity.ExcludePokemonIdsJson = JsonSerializer.Serialize(state.ExcludePokemonIds, JsonOptions);
            entity.TrackedUidsJson = JsonSerializer.Serialize(state.TrackedUids, JsonOptions);
        }

        await this._context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string userId, int profileNo, string quickPickId)
    {
        var entity = await this._context.QuickPickAppliedStates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ProfileNo == profileNo && s.QuickPickId == quickPickId);

        if (entity is not null)
        {
            this._context.QuickPickAppliedStates.Remove(entity);
            await this._context.SaveChangesAsync();
        }
    }

    public async Task DeleteByQuickPickIdAsync(string quickPickId, string? userId = null)
    {
        var query = this._context.QuickPickAppliedStates.Where(s => s.QuickPickId == quickPickId);

        if (userId is not null)
        {
            query = query.Where(s => s.UserId == userId);
        }

        // Loaded and removed rather than ExecuteDeleteAsync: the provider emits an aliased
        // DELETE ... AS `q`, which MariaDB rejects outright.
        var rows = await query.ToListAsync();

        if (rows.Count == 0)
        {
            return;
        }

        this._context.QuickPickAppliedStates.RemoveRange(rows);
        await this._context.SaveChangesAsync();
    }

    public async Task DeleteByUserAsync(string userId)
    {
        var rows = await this._context.QuickPickAppliedStates.Where(s => s.UserId == userId).ToListAsync();

        if (rows.Count == 0)
        {
            return;
        }

        this._context.QuickPickAppliedStates.RemoveRange(rows);
        await this._context.SaveChangesAsync();
    }

    private static QuickPickAppliedState MapToModel(QuickPickAppliedStateEntity entity) => new()
    {
        UserId = entity.UserId,
        ProfileNo = entity.ProfileNo,
        QuickPickId = entity.QuickPickId,
        AlarmType = entity.AlarmType,
        AppliedAt = entity.AppliedAt,
        ExcludePokemonIds = string.IsNullOrEmpty(entity.ExcludePokemonIdsJson)
                ? []
                : JsonSerializer.Deserialize<List<int>>(entity.ExcludePokemonIdsJson, JsonOptions) ?? [],
        TrackedUids = string.IsNullOrEmpty(entity.TrackedUidsJson)
                ? []
                : JsonSerializer.Deserialize<List<int>>(entity.TrackedUidsJson, JsonOptions) ?? [],
    };
}
