using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data.Scanner;

namespace Pgan.PoracleWebNet.Core.Services;

public class RdmScannerService(RdmScannerContext context) : IScannerService
{
    private readonly RdmScannerContext _context = context;

    public async Task<IEnumerable<QuestData>> GetActiveQuestsAsync() => await this._context.Pokestops
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

    public async Task<IEnumerable<RaidData>> GetActiveRaidsAsync()
    {
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return await this._context.Gyms
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

    public async Task<GymSearchResult?> GetGymByIdAsync(string gymId)
    {
        var gym = await this._context.Gyms
            .AsNoTracking()
            .Where(g => g.Id == gymId)
            .Select(g => new GymSearchResult
            {
                Id = g.Id,
                Name = g.Name,
                Url = g.Url,
                Lat = g.Lat,
                Lon = g.Lon,
                TeamId = g.TeamId
            })
            .FirstOrDefaultAsync();

        return gym;
    }

    public async Task<IEnumerable<int>> GetMaxBattlePokemonIdsAsync() => await this._context.Stations
            .AsNoTracking()
            .Where(s => s.BattlePokemonId != null && s.BattlePokemonId > 0)
            .Select(s => s.BattlePokemonId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync();

    public async Task<WeatherData?> GetWeatherAtLocationAsync(double lat, double lon)
    {
        var cellId = S2CellHelper.LatLonToWeatherCellId(lat, lon);
        var weather = await this._context.Weather
            .AsNoTracking()
            .Where(w => w.Id == cellId)
            .FirstOrDefaultAsync();

        if (weather == null)
        {
            return null;
        }

        return WeatherData.FromCondition(
            weather.GameplayCondition ?? 0,
            weather.Severity ?? 0,
            (weather.WarnWeather ?? 0) > 0,
            weather.Updated);
    }

    public async Task<Dictionary<long, WeatherData>> GetWeatherForCellsAsync(IEnumerable<long> cellIds)
    {
        var uniqueIds = cellIds.Distinct().ToList();
        if (uniqueIds.Count == 0)
        {
            return [];
        }

        var rows = await this._context.Weather
            .AsNoTracking()
            .Where(w => uniqueIds.Contains(w.Id))
            .ToListAsync();

        return rows.ToDictionary(
            w => w.Id,
            w => WeatherData.FromCondition(
                w.GameplayCondition ?? 0,
                w.Severity ?? 0,
                (w.WarnWeather ?? 0) > 0,
                w.Updated));
    }

    public async Task<IEnumerable<GymSearchResult>> SearchGymsAsync(string search, int limit = 20)
    {
        var query = this._context.Gyms
            .AsNoTracking()
            .Where(g => g.Name != null && EF.Functions.Like(g.Name, $"%{search}%"))
            .OrderBy(g => g.Name)
            .Take(limit);

        return await query
            .Select(g => new GymSearchResult
            {
                Id = g.Id,
                Name = g.Name,
                Url = g.Url,
                Lat = g.Lat,
                Lon = g.Lon,
                TeamId = g.TeamId
            })
            .ToListAsync();
    }
}
