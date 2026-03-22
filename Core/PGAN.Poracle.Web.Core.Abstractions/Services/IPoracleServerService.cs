using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IPoracleServerService
{
    public Task<List<PoracleServerStatus>> GetServersAsync();
    public Task<PoracleServerStatus> RestartServerAsync(string host);
    public Task<List<PoracleServerStatus>> RestartAllAsync();
    public Task UpdateGroupMapAsync(string geofenceName, string group);
}
