using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IScannerService
{
    public Task<IEnumerable<QuestData>> GetActiveQuestsAsync();
    public Task<IEnumerable<RaidData>> GetActiveRaidsAsync();
    public Task<IEnumerable<GymSearchResult>> SearchGymsAsync(string search, int limit = 20);
    public Task<GymSearchResult?> GetGymByIdAsync(string gymId);
    public Task<IEnumerable<int>> GetMaxBattlePokemonIdsAsync();
    public Task<WeatherData?> GetWeatherAtLocationAsync(double lat, double lon);
    public Task<Dictionary<long, WeatherData>> GetWeatherForCellsAsync(IEnumerable<long> cellIds);
}
