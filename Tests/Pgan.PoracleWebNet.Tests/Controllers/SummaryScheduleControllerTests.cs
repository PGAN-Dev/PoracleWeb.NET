using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class SummaryScheduleControllerTests : ControllerTestBase
{
    private const string UserId = "123456789";

    private readonly Mock<IPoracleSummaryProxy> _proxy = new();
    private readonly Mock<ISummaryCapabilityService> _capability = new();
    private readonly SummaryScheduleController _sut;
    private static readonly string[] expected = ["ActiveHours"];

    public SummaryScheduleControllerTests()
    {
        this._sut = new SummaryScheduleController(this._proxy.Object, this._capability.Object);
        SetupUser(this._sut, userId: UserId);
    }

    // ──────────────────────────────────────────────────────────────
    // GetCapability — 200 { enabled = bool }, never 5xx
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCapabilityReturnsEnabledBoolean(bool enabled)
    {
        this._capability.Setup(c => c.IsQuestSummaryEnabledAsync()).ReturnsAsync(enabled);

        var result = await this._sut.GetCapability();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        var flag = (bool?)ok.Value.GetType().GetProperty("enabled")?.GetValue(ok.Value);
        Assert.Equal(enabled, flag);
    }

    // ──────────────────────────────────────────────────────────────
    // GetSchedules — maps proxy array, NEVER projects upstream id
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSchedulesUsesJwtUserIdAndReturnsOk()
    {
        var schedules = JsonDocument.Parse(
            /*lang=json,strict*/ """[{"id":"123456789","alert_type":"quest","active_hours":"[{\"day\":1,\"hours\":9,\"mins\":0}]"}]""").RootElement;
        this._proxy.Setup(p => p.GetSchedulesAsync(UserId)).ReturnsAsync(schedules);

        var result = await this._sut.GetSchedules();

        Assert.IsType<OkObjectResult>(result);
        // IDOR guard: the JWT user id is the only id passed to the proxy.
        this._proxy.Verify(p => p.GetSchedulesAsync(UserId), Times.Once);
    }

    [Fact]
    public async Task GetSchedulesDoesNotEchoUpstreamId()
    {
        var schedules = JsonDocument.Parse(
            /*lang=json,strict*/ """[{"id":"123456789","alert_type":"quest","active_hours":"[]"}]""").RootElement;
        this._proxy.Setup(p => p.GetSchedulesAsync(UserId)).ReturnsAsync(schedules);

        var result = await this._sut.GetSchedules();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.Serialize(ok.Value);
        // The upstream "id" is the user id — it must never be echoed back to the client.
        Assert.DoesNotContain("123456789", payload, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────
    // GetSchedule — validates alertType, 200 empty schedule on null proxy
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetScheduleValidTypeReturnsOk()
    {
        var schedule = JsonDocument.Parse(
            /*lang=json,strict*/ """{"id":"123456789","alert_type":"quest","active_hours":"[]"}""").RootElement;
        this._proxy.Setup(p => p.GetScheduleAsync(UserId, "quest")).ReturnsAsync(schedule);

        var result = await this._sut.GetSchedule("quest");

        Assert.IsType<OkObjectResult>(result);
        this._proxy.Verify(p => p.GetScheduleAsync(UserId, "quest"), Times.Once);
    }

    [Fact]
    public async Task GetScheduleProxyNullReturnsEmptyScheduleNot404()
    {
        // "No schedule yet" is a normal empty state — return 200 with an empty schedule so the
        // SPA's global 404 toast does not fire when a user first opens the dialog.
        this._proxy.Setup(p => p.GetScheduleAsync(UserId, "quest")).ReturnsAsync((JsonElement?)null);

        var result = await this._sut.GetSchedule("quest");

        var ok = Assert.IsType<OkObjectResult>(result);
        var schedule = Assert.IsType<SummarySchedule>(ok.Value);
        Assert.Equal("quest", schedule.AlertType);
        Assert.Equal("[]", schedule.ActiveHours);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("pokemon")]
    [InlineData("")]
    public async Task GetScheduleInvalidTypeReturnsBadRequestBeforeProxy(string alertType)
    {
        var result = await this._sut.GetSchedule(alertType);

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.GetScheduleAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetScheduleAlertTypeIsCaseInsensitive()
    {
        var schedule = JsonDocument.Parse(/*lang=json,strict*/ """{"alert_type":"quest","active_hours":"[]"}""").RootElement;
        this._proxy.Setup(p => p.GetScheduleAsync(UserId, "QUEST")).ReturnsAsync(schedule);

        var result = await this._sut.GetSchedule("QUEST");

        Assert.IsType<OkObjectResult>(result);
    }

    // ──────────────────────────────────────────────────────────────
    // SetSchedule — validates alertType + active_hours BEFORE proxy
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetScheduleValidReturnsNoContent()
    {
        this._proxy.Setup(p => p.SetScheduleAsync(UserId, "quest", It.IsAny<string>())).Returns(Task.CompletedTask);
        var request = new SummaryScheduleRequest { ActiveHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0}]" };

        var result = await this._sut.SetSchedule("quest", request);

        Assert.IsType<NoContentResult>(result);
        this._proxy.Verify(p => p.SetScheduleAsync(UserId, "quest", request.ActiveHours), Times.Once);
    }

    [Fact]
    public async Task SetScheduleNullActiveHoursClearsWithEmptyArray()
    {
        this._proxy.Setup(p => p.SetScheduleAsync(UserId, "quest", It.IsAny<string>())).Returns(Task.CompletedTask);
        var request = new SummaryScheduleRequest { ActiveHours = null };

        var result = await this._sut.SetSchedule("quest", request);

        Assert.IsType<NoContentResult>(result);
        // null/whitespace = clear -> "[]"
        this._proxy.Verify(p => p.SetScheduleAsync(UserId, "quest", "[]"), Times.Once);
    }

    [Fact]
    public async Task SetScheduleInvalidActiveHoursReturnsBadRequestBeforeProxy()
    {
        // day 8 is out of range — the shared ActiveHoursValidator must reject before any proxy call.
        var request = new SummaryScheduleRequest { ActiveHours = /*lang=json,strict*/ "[{\"day\":8,\"hours\":9,\"mins\":0}]" };

        var result = await this._sut.SetSchedule("quest", request);

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.SetScheduleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetScheduleTooManyEntriesReturnsBadRequestBeforeProxy()
    {
        // 29 entries exceeds the load-bearing ≤28 cap (keeps payload inside the varchar(4096) column).
        var entries = string.Join(",", Enumerable.Range(0, 29).Select(i =>
            $"{{\"day\":{(i % 7) + 1},\"hours\":{i % 24},\"mins\":0}}"));
        var request = new SummaryScheduleRequest { ActiveHours = $"[{entries}]" };

        var result = await this._sut.SetSchedule("quest", request);

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.SetScheduleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetScheduleInvalidTypeReturnsBadRequestBeforeProxy()
    {
        var request = new SummaryScheduleRequest { ActiveHours = "[]" };

        var result = await this._sut.SetSchedule("pokemon", request);

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.SetScheduleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteSchedule — idempotent, validates alertType
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteScheduleValidReturnsNoContent()
    {
        this._proxy.Setup(p => p.DeleteScheduleAsync(UserId, "quest")).Returns(Task.CompletedTask);

        var result = await this._sut.DeleteSchedule("quest");

        Assert.IsType<NoContentResult>(result);
        this._proxy.Verify(p => p.DeleteScheduleAsync(UserId, "quest"), Times.Once);
    }

    [Fact]
    public async Task DeleteScheduleInvalidTypeReturnsBadRequestBeforeProxy()
    {
        var result = await this._sut.DeleteSchedule("invalid");

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.DeleteScheduleAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────
    // Trigger — validates alertType, uses JWT id
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerValidReturnsNoContent()
    {
        this._proxy.Setup(p => p.TriggerAsync(UserId, "quest")).Returns(Task.CompletedTask);

        var result = await this._sut.Trigger("quest");

        Assert.IsType<NoContentResult>(result);
        // Trigger delivers a real DM — the JWT user id is the only target (no path/body id).
        this._proxy.Verify(p => p.TriggerAsync(UserId, "quest"), Times.Once);
    }

    [Fact]
    public async Task TriggerInvalidTypeReturnsBadRequestBeforeProxy()
    {
        var result = await this._sut.Trigger("pokemon");

        Assert.IsType<BadRequestObjectResult>(result);
        this._proxy.Verify(p => p.TriggerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────
    // IDOR: no id route segment / no id body field
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ControllerDerivesFromBaseApiControllerForJwtUserIdSource() =>
        // BaseApiController.UserId reads the JWT — guarantees there is no controller-level id parameter to spoof.
        Assert.True(typeof(BaseApiController).IsAssignableFrom(typeof(SummaryScheduleController)));

    [Fact]
    public void NoActionMethodHasAUserIdOrIdRouteParameter()
    {
        // Every {alertType} action must derive the human id from the JWT, never from a route segment.
        var actions = typeof(SummaryScheduleController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var action in actions)
        {
            foreach (var p in action.GetParameters())
            {
                Assert.False(
                    p.Name is "id" or "userId",
                    $"Action {action.Name} must not accept an '{p.Name}' parameter (IDOR risk).");
            }
        }
    }

    [Fact]
    public void SetScheduleRequestDtoExposesOnlyActiveHours()
    {
        // The PUT body must carry ONLY ActiveHours — any id/userId/alertType body field is an IDOR vector.
        var props = typeof(SummaryScheduleRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(expected, props);
    }

    // ──────────────────────────────────────────────────────────────
    // Attribute wiring: feature gate, rate limits, base policies
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ControllerIsRouted()
    {
        // inherit: false — BaseApiController also declares [Route], which would make the
        // inherited single-attribute lookup ambiguous; we want the controller's own template.
        var route = typeof(SummaryScheduleController).GetCustomAttribute<RouteAttribute>(inherit: false);
        Assert.NotNull(route);
        Assert.Equal("api/summary-schedules", route!.Template);
    }

    /// <summary>
    /// Setting a schedule and firing one on demand are gated; reading and deleting are not. A schedule
    /// for a disabled type delivers nothing, so removing it is the one useful thing left, and gating
    /// the reads would hide a schedule its owner can no longer see or clear.
    /// </summary>
    [Theory]
    [InlineData(nameof(SummaryScheduleController.SetSchedule))]
    [InlineData(nameof(SummaryScheduleController.Trigger))]
    public void WritesAreGatedByDisableQuests(string action)
    {
        // #236 lesson: the controller filter is the real boundary, not the Angular guard.
        var attr = typeof(SummaryScheduleController).GetMethod(action)!
            .GetCustomAttribute<RequireFeatureEnabledAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("disable_quests", (string)attr!.Arguments![0]);
    }

    [Theory]
    [InlineData(nameof(SummaryScheduleController.GetSchedules))]
    [InlineData(nameof(SummaryScheduleController.GetSchedule))]
    [InlineData(nameof(SummaryScheduleController.GetCapability))]
    [InlineData(nameof(SummaryScheduleController.DeleteSchedule))]
    public void ReadsAndDeleteStayOpen(string action)
    {
        var attr = typeof(SummaryScheduleController).GetMethod(action)!
            .GetCustomAttribute<RequireFeatureEnabledAttribute>();

        Assert.Null(attr);
    }

    [Fact]
    public void ControllerHasNoClassLevelGate()
    {
        var attr = typeof(SummaryScheduleController).GetCustomAttribute<RequireFeatureEnabledAttribute>();

        Assert.True(attr is null, "A class-level gate 403s the reads and the delete too.");
    }

    [Fact]
    public void ControllerRequiresAuthorizationViaBase()
    {
        // [Authorize] is inherited from BaseApiController; ensure it is present on the type chain.
        var attr = typeof(SummaryScheduleController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(attr);
    }

    [Fact]
    public void TriggerActionHasTestAlertRateLimitPolicy()
    {
        // Trigger delivers a real DM — a double-click must not double-deliver. 5/60s "test-alert" partitioned policy.
        var method = typeof(SummaryScheduleController).GetMethod(nameof(SummaryScheduleController.Trigger));
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("test-alert", attr!.PolicyName);
    }

    [Theory]
    [InlineData(nameof(SummaryScheduleController.SetSchedule))]
    [InlineData(nameof(SummaryScheduleController.DeleteSchedule))]
    public void WriteActionsHaveAuthReadRateLimitPolicy(string methodName)
    {
        // PUT/DELETE arm a debounced upstream reload — they carry the 120/60s "auth-read" partitioned policy.
        var method = typeof(SummaryScheduleController).GetMethod(methodName);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("auth-read", attr!.PolicyName);
    }
}
