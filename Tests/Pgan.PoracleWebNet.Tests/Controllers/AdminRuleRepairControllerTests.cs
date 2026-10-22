using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AdminRuleRepairControllerTests : ControllerTestBase
{
    private readonly Mock<IRuleRepairJob> _job = new();
    private readonly AdminRuleRepairController _sut;

    private static readonly RuleRepairStatus Scanned = RuleRepairStatus.Idle with { Phase = RuleRepairPhase.Scanned };

    public AdminRuleRepairControllerTests()
    {
        this._sut = new AdminRuleRepairController(this._job.Object);
        this._job.Setup(j => j.GetStatus()).Returns(Scanned);
    }

    [Fact]
    public void NonAdminIsForbidden()
    {
        SetupUser(this._sut, isAdmin: false);

        Assert.IsType<ForbidResult>(this._sut.GetStatus());
        Assert.IsType<ForbidResult>(this._sut.Scan());
        Assert.IsType<ForbidResult>(this._sut.Apply());
        this._job.Verify(j => j.TryStartScan(), Times.Never);
        this._job.Verify(j => j.TryStartApply(), Times.Never);
    }

    [Fact]
    public void ImpersonatingIsForbidden()
    {
        SetupImpersonatingUser(this._sut, isAdmin: true);

        Assert.IsType<ForbidResult>(this._sut.GetStatus());
        Assert.IsType<ForbidResult>(this._sut.Scan());
        Assert.IsType<ForbidResult>(this._sut.Apply());
        this._job.Verify(j => j.TryStartScan(), Times.Never);
        this._job.Verify(j => j.TryStartApply(), Times.Never);
    }

    [Fact]
    public void AdminReadsTheStatus()
    {
        SetupUser(this._sut, isAdmin: true);

        var ok = Assert.IsType<OkObjectResult>(this._sut.GetStatus());
        Assert.Same(Scanned, ok.Value);
    }

    [Fact]
    public void ScanStartsAndAnswers202()
    {
        SetupUser(this._sut, isAdmin: true);
        this._job.Setup(j => j.TryStartScan()).Returns(true);

        var accepted = Assert.IsType<AcceptedResult>(this._sut.Scan());
        Assert.Same(Scanned, accepted.Value);
    }

    [Fact]
    public void ScanWhileBusyIs409WithTheCurrentState()
    {
        SetupUser(this._sut, isAdmin: true);
        this._job.Setup(j => j.TryStartScan()).Returns(false);

        var conflict = Assert.IsType<ConflictObjectResult>(this._sut.Scan());
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Same(Scanned, conflict.Value);
    }

    [Fact]
    public void ApplyAfterScanAnswers202()
    {
        SetupUser(this._sut, isAdmin: true);
        this._job.Setup(j => j.TryStartApply()).Returns(true);

        Assert.IsType<AcceptedResult>(this._sut.Apply());
    }

    [Fact]
    public void ApplyBeforeScanIs409()
    {
        SetupUser(this._sut, isAdmin: true);
        this._job.Setup(j => j.GetStatus()).Returns(RuleRepairStatus.Idle);
        this._job.Setup(j => j.TryStartApply()).Returns(false);

        var conflict = Assert.IsType<ConflictObjectResult>(this._sut.Apply());
        Assert.Same(RuleRepairStatus.Idle, conflict.Value);
    }
}
