using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data.Scanner;

namespace PGAN.Poracle.Web.Core.Services;

public class RdmScannerService : IScannerService
{
    private readonly RdmScannerContext _context;

    public RdmScannerService(RdmScannerContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<QuestData>> GetActiveQuestsAsync()
    {
        return await _context.Pokestops
            .AsNoTracking()
            .Where(p => p.QuestType != null)
            .Select(p => new QuestData
            {
                PokestopId = p.Id,
                Name = p.Name,
                Lat = p.Lat,
                Lon = p.Lon,
                QuestType = p.QuestType ?? 0,
                RewardType = p.QuestRewardType ?? 0,
                RewardId = p.QuestRewardType == 2
                    ? (p.QuestItemId ?? 0)
                    : (p.QuestPokemonId ?? 0)
            })
            .ToListAsync();
    }

    public async Task<IEnumerable<RaidData>> GetActiveRaidsAsync()
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return await _context.Gyms
            .AsNoTracking()
            .Where(g => g.RaidEndTimestamp != null && g.RaidEndTimestamp > nowUnix)
            .Select(g => new RaidData
            {
                GymId = g.Id,
                Name = g.Name,
                Lat = g.Lat,
                Lon = g.Lon,
                Level = g.RaidLevel ?? 0,
                PokemonId = g.RaidPokemonId ?? 0,
                Form = g.RaidPokemonForm ?? 0,
                EndTime = DateTimeOffset.FromUnixTimeSeconds(g.RaidEndTimestamp ?? 0)
            })
            .ToListAsync();
    }
}
