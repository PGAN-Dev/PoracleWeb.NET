using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/scanner")]
public class ScannerController(IScannerService? scannerService = null) : BaseApiController
{
    private readonly IScannerService? _scannerService = scannerService;

    [HttpGet("quests")]
    public async Task<IActionResult> GetActiveQuests()
    {
        if (this._scannerService == null)
        {
            return this.NotFound(new
            {
                message = "Scanner database not configured."
            });
        }

        var quests = await this._scannerService.GetActiveQuestsAsync();
        return this.Ok(quests);
    }

    [HttpGet("raids")]
    public async Task<IActionResult> GetActiveRaids()
    {
        if (this._scannerService == null)
        {
            return this.NotFound(new
            {
                message = "Scanner database not configured."
            });
        }

        var raids = await this._scannerService.GetActiveRaidsAsync();
        return this.Ok(raids);
    }
}
