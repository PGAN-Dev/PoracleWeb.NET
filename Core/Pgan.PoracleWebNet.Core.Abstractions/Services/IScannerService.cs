using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IScannerService
{
    public Task<IEnumerable<QuestData>> GetActiveQuestsAsync();
    public Task<IEnumerable<RaidData>> GetActiveRaidsAsync();
    public Task<IEnumerable<GymSearchResult>> SearchGymsAsync(string search, int limit = 20);
    public Task<GymSearchResult?> GetGymByIdAsync(string gymId);
    public Task<IEnumerable<int>> GetMaxBattlePokemonIdsAsync();

    /// <summary>
    /// Tests if a point (lat, lon) is inside a polygon using the ray-casting algorithm.
    /// Polygon is double[][] where each entry is [lat, lon].
    /// </summary>
    public static bool PointInPolygon(double lat, double lon, double[][] polygon)
    {
        var n = polygon.Length;
        if (n < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var yi = polygon[i][0];
            var xi = polygon[i][1];
            var yj = polygon[j][0];
            var xj = polygon[j][1];
            if (((yi > lat) != (yj > lat)) &&
                (lon < ((xj - xi) * (lat - yi) / (yj - yi)) + xi))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
