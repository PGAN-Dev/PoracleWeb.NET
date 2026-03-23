using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class QuickPickAppliedStateRepository(PoracleWebContext context, IMapper mapper) : IQuickPickAppliedStateRepository
{
    private readonly PoracleWebContext _context = context;
    private readonly IMapper _mapper = mapper;

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

    private static QuickPickAppliedState MapToModel(QuickPickAppliedStateEntity entity) => new QuickPickAppliedState
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
