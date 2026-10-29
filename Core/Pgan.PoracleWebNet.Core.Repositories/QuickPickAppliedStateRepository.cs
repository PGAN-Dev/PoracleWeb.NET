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
        // One atomic upsert instead of read-then-insert, so two writers racing on the same
        // (user, profile, pick) never raise a "Duplicate entry" error. user_id can hold a webhook's id
        // (its URL) when a webhook is managed, and that error text is logged. The update branch leaves
        // alarm_type alone, as it always has. See #1101.
        var appliedAt = DateTime.UtcNow;
        var excludeJson = JsonSerializer.Serialize(state.ExcludePokemonIds, JsonOptions);
        var trackedJson = JsonSerializer.Serialize(state.TrackedUids, JsonOptions);

        if (this._context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            // The tests run on SQLite, which spells the same thing differently.
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO quick_pick_applied_states (user_id, profile_no, quick_pick_id, alarm_type, applied_at, exclude_pokemon_ids_json, tracked_uids_json)
VALUES ({state.UserId}, {state.ProfileNo}, {state.QuickPickId}, {state.AlarmType}, {appliedAt}, {excludeJson}, {trackedJson})
ON CONFLICT (user_id, profile_no, quick_pick_id) DO UPDATE SET
  applied_at = excluded.applied_at,
  exclude_pokemon_ids_json = excluded.exclude_pokemon_ids_json,
  tracked_uids_json = excluded.tracked_uids_json");
        }
        else
        {
            // VALUES(col) rather than the row-alias form: MariaDB has no alias syntax. MySQL 8 only warns.
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO quick_pick_applied_states (user_id, profile_no, quick_pick_id, alarm_type, applied_at, exclude_pokemon_ids_json, tracked_uids_json)
VALUES ({state.UserId}, {state.ProfileNo}, {state.QuickPickId}, {state.AlarmType}, {appliedAt}, {excludeJson}, {trackedJson})
ON DUPLICATE KEY UPDATE
  applied_at = VALUES(applied_at),
  exclude_pokemon_ids_json = VALUES(exclude_pokemon_ids_json),
  tracked_uids_json = VALUES(tracked_uids_json)");
        }
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
