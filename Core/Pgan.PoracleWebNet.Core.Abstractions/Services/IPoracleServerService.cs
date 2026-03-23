using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleServerService
{
    public Task<List<PoracleServerStatus>> GetServersAsync();
    public Task<PoracleServerStatus> RestartServerAsync(string host);
    public Task<List<PoracleServerStatus>> RestartAllAsync();
    public Task UpdateGroupMapAsync(string geofenceName, string group);
}
