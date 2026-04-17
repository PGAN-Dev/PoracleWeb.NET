using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/test-alert")]
[EnableRateLimiting("test-alert")]
public partial class TestAlertController(
    ITestAlertService testAlertService,
    IFeatureGate featureGate,
    ILogger<TestAlertController> logger) : BaseApiController
{
    private static readonly HashSet<string> ValidTypes = ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    private readonly ILogger<TestAlertController> _logger = logger;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ITestAlertService _testAlertService = testAlertService;

    [HttpPost("{type}/{uid:int}")]
    public async Task<IActionResult> SendTestAlert(string type, int uid)
    {
        if (!ValidTypes.Contains(type))
        {
            return this.BadRequest(new
            {
                error = $"Invalid alarm type: {type}"
            });
        }

        // Reuses the centralized type→disable-key map and the same FeatureGate the alarm services
        // and resource filter use — so a future tweak (new alarm type, key rename) only touches
        // DisableFeatureKeys, not this controller. Throws FeatureDisabledException → global
        // FeatureDisabledExceptionFilter returns 403 with disableKey body. (#236)
        if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var disableKey))
        {
            await this._featureGate.EnsureEnabledAsync(disableKey);
        }

        try
        {
            await this._testAlertService.SendTestAlertAsync(this.UserId, type, uid);
            return this.Ok(new
            {
                status = "ok",
                message = "Test alert sent"
            });
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound(new
            {
                error = "Alarm not found"
            });
        }
        catch (NotSupportedException ex)
        {
            // Alarm types with no upstream /api/test surface (currently: nest).
            return this.StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = ex.Message
            });
        }
        catch (OperationCanceledException ex)
        {
            LogSendTestAlertFailed(this._logger, ex, type, uid, this.UserId);
            return this.BadRequest(new
            {
                error = "Test alert request was canceled"
            });
        }
        catch (Exception ex)
        {
            LogSendTestAlertFailed(this._logger, ex, type, uid, this.UserId);
            return this.StatusCode(500, new
            {
                error = "Failed to send test alert"
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send test alert: type={Type}, uid={Uid}, user={UserId}")]
    private static partial void LogSendTestAlertFailed(ILogger logger, Exception ex, string type, int uid, string userId);
}
