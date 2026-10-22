using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Scans every human's stored rules for the ones PoracleNG 5.4.0 reads differently, and repairs them.
/// </summary>
/// <remarks>
/// The work runs in <see cref="IRuleRepairJob"/>, a singleton on its own background task, so these actions
/// only start it and report on it; none of them waits on PoracleNG. Admin-only, and refused while
/// impersonating: <see cref="BaseApiController.UserId"/> would be the inspected account, and the job
/// writes to everybody's rules.
/// </remarks>
[Route("api/admin/rule-repair")]
public sealed partial class AdminRuleRepairController(IRuleRepairJob job, ILogger<AdminRuleRepairController> logger) : BaseApiController
{
    private readonly IRuleRepairJob _job = job;
    private readonly ILogger<AdminRuleRepairController> _logger = logger;

    [HttpGet]
    public IActionResult GetStatus() => this.Refused ? this.Forbid() : this.Ok(this._job.GetStatus());

    /// <summary>Starts a scan. 202 with the new state, or 409 with the current one while a job runs.</summary>
    [HttpPost("scan")]
    public IActionResult Scan() => this.Refused ? this.Forbid() : this.Started("scan", this._job.TryStartScan());

    /// <summary>Applies the last scan. 202, or 409 while a job runs or when nothing has been scanned.</summary>
    [HttpPost("apply")]
    public IActionResult Apply() => this.Refused ? this.Forbid() : this.Started("apply", this._job.TryStartApply());

    private bool Refused => !this.IsAdmin || this.IsImpersonating;

    private IActionResult Started(string run, bool started)
    {
        if (!started)
        {
            return this.Conflict(this._job.GetStatus());
        }

        // A bulk write to every account: say who asked for it.
        LogStarted(this._logger, run, this.UserId, this.Username);
        return this.Accepted(this._job.GetStatus());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rule repair {Run} started by admin {UserId} ({Username}).")]
    private static partial void LogStarted(ILogger logger, string run, string userId, string username);
}
