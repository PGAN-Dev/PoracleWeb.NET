using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IGeoJsonService
{
    public Task<GeoJsonFeatureCollection> ExportAsync(string userId);
    public Task<GeoJsonImportResult> ImportAsync(string userId, int profileNo, Stream geoJsonStream);
}
