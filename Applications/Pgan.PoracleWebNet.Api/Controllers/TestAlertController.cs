using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/test-alert")]
[EnableRateLimiting("test-alert")]
public partial class TestAlertController(
    ITestAlertService testAlertService,
    ISiteSettingService siteSettings,
    ILogger<TestAlertController> logger) : BaseApiController
{
    private static readonly HashSet<string> ValidTypes = ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    /// <summary>
    /// Maps alarm type → site-setting disable key. Eggs share the raid disable toggle
    /// since they share the raid UI. See #236.
    /// </summary>
    private static readonly Dictionary<string, string> DisableKeys = new(StringComparer.Ordinal)
    {
        ["pokemon"] = DisableFeatureKeys.Pokemon,
        ["raid"] = DisableFeatureKeys.Raids,
        ["egg"] = DisableFeatureKeys.Raids,
        ["quest"] = DisableFeatureKeys.Quests,
        ["invasion"] = DisableFeatureKeys.Invasions,
        ["lure"] = DisableFeatureKeys.Lures,
        ["nest"] = DisableFeatureKeys.Nests,
        ["gym"] = DisableFeatureKeys.Gyms,
    };

    private readonly ILogger<TestAlertController> _logger = logger;
    private readonly ISiteSettingService _siteSettings = siteSettings;
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

        if (DisableKeys.TryGetValue(type, out var disableKey) && await this._siteSettings.GetBoolAsync(disableKey))
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "This feature is disabled by the administrator.",
                disableKey
            });
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
