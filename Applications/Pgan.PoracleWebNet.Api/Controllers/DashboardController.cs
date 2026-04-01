using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboardService) : BaseApiController
{
    private readonly IDashboardService _dashboardService = dashboardService;

    [HttpGet]
    public async Task<IActionResult> GetCounts()
    {
        var counts = await this._dashboardService.GetCountsAsync(this.UserId, this.ProfileNo);
        return this.Ok(counts);
    }
}
