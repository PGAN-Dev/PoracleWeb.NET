using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Abstractions.Services;

public interface IPoracleServerService
{
    Task<List<PoracleServerStatus>> GetServersAsync();
    Task<PoracleServerStatus> RestartServerAsync(string host);
    Task<List<PoracleServerStatus>> RestartAllAsync();
}
