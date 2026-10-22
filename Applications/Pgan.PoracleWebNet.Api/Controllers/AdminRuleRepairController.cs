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
public sealed class AdminRuleRepairController(IRuleRepairJob job) : BaseApiController
{
    private readonly IRuleRepairJob _job = job;

    [HttpGet]
    public IActionResult GetStatus() => this.Refused ? this.Forbid() : this.Ok(this._job.GetStatus());

    /// <summary>Starts a scan. 202 with the new state, or 409 with the current one while a job runs.</summary>
    [HttpPost("scan")]
    public IActionResult Scan() => this.Refused ? this.Forbid() : this.Started(this._job.TryStartScan());

    /// <summary>Applies the last scan. 202, or 409 while a job runs or when nothing has been scanned.</summary>
    [HttpPost("apply")]
    public IActionResult Apply() => this.Refused ? this.Forbid() : this.Started(this._job.TryStartApply());

    private bool Refused => !this.IsAdmin || this.IsImpersonating;

    private IActionResult Started(bool started) =>
        started ? this.Accepted(this._job.GetStatus()) : this.Conflict(this._job.GetStatus());
}
