using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/scanner")]
public class ScannerController : BaseApiController
{
    private readonly IScannerService? _scannerService;

    public ScannerController(IScannerService? scannerService = null)
    {
        _scannerService = scannerService;
    }

    [HttpGet("quests")]
    public async Task<IActionResult> GetActiveQuests()
    {
        if (_scannerService == null)
            return NotFound(new { message = "Scanner database not configured." });

        var quests = await _scannerService.GetActiveQuestsAsync();
        return Ok(quests);
    }

    [HttpGet("raids")]
    public async Task<IActionResult> GetActiveRaids()
    {
        if (_scannerService == null)
            return NotFound(new { message = "Scanner database not configured." });

        var raids = await _scannerService.GetActiveRaidsAsync();
        return Ok(raids);
    }
}
