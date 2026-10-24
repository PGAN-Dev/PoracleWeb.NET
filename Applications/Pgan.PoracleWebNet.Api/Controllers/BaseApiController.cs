using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    protected string UserId => this.User.FindFirstValue("userId") ?? throw new UnauthorizedAccessException();
    protected int ProfileNo => int.Parse(this.User.FindFirstValue("profileNo") ?? "1", CultureInfo.InvariantCulture);
    protected bool IsAdmin => this.User.FindFirstValue("isAdmin") == "true";

    /// <summary>
    /// True when the caller is an admin (or webhook delegate) inspecting somebody else's account, so
    /// <see cref="UserId"/> names the account being looked at rather than the person looking.
    /// </summary>
    /// <remarks>
    /// Only <c>GenerateImpersonationToken</c> sets the claim, and the JWT is signed, so an inspected
    /// user cannot mint one for themselves. Every decision about the CALLER -- their admin rights,
    /// whether their session survives -- must consult this before reading the effective id, or it
    /// answers a question about the wrong person. See #663, #706.
    /// </remarks>
    protected bool IsImpersonating => this.User.FindFirst("impersonatedBy") is not null;
    protected string Username => this.User.FindFirstValue("username") ?? string.Empty;
    protected string[] ManagedDestinations => this.User.FindFirstValue("managedWebhooks")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>
    /// Rejects a distance the create path would have rejected. The "update all" endpoints bind a bare
    /// <c>[FromBody] int</c>, which model validation cannot annotate, so the check has to be explicit.
    /// Returns <c>null</c> when the value is acceptable. See #417.
    /// </summary>
    protected IActionResult? RejectInvalidDistance(int distance) =>
        distance is < 0 or > AlarmDistance.MaxMetres
            ? this.BadRequest(new
            {
                error = $"Distance must be between 0 and {AlarmDistance.MaxMetres} metres."
            })
            : null;

    /// <summary>
    /// The answer to a bulk "Update Distance". <c>updated</c> keeps its meaning for existing callers;
    /// the two scope skip lists name the selected alarms a radius could not apply to, and
    /// <c>skippedConflict</c> the ones PoracleNG left alone because they would collide with another alarm,
    /// so the SPA can say why they did not change. See <see cref="DistanceUpdateResult"/>.
    /// </summary>
    protected IActionResult DistanceUpdated(DistanceUpdateResult result) =>
        this.Ok(new
        {
            updated = result.Updated,
            skippedAreaScoped = result.SkippedAreaScoped,
            skippedPlaceScoped = result.SkippedPlaceScoped,
            skippedConflict = result.SkippedConflict,
        });

    /// <summary>
    /// True when applying an update leaves the stored alarm exactly as it was.
    /// </summary>
    /// <remarks>
    /// Every edit dialog resubmits the whole form, so pressing Save with nothing changed sends the values
    /// already stored. PoracleNG answers that with {alreadyPresent:1, insert:0, updates:0} -- the same
    /// shape it uses when the edit collides with a DIFFERENT alarm -- which #463 turned into a 409 telling
    /// the user another alarm was in the way when the only candidate was itself. Detected here, where both
    /// sides are in hand, rather than guessed at from the response. Nothing to write means nothing to send.
    /// See #498, #499, #501.
    /// </remarks>
    protected static bool LeavesAlarmUnchanged<TAlarm>(TAlarm before, Func<TAlarm> applyUpdate)
    {
        var original = JsonSerializer.Serialize(before);
        return JsonSerializer.Serialize(applyUpdate()) == original;
    }

    /// <summary>Checks that <paramref name="ownerId"/> matches the authenticated user. Returns true when NOT owned (i.e. should return 404).</summary>
    protected bool NotOwnedByCurrentUser(string? ownerId) => !string.Equals(ownerId, this.UserId, StringComparison.Ordinal);

    /// <summary>The refusal both move paths give while another account is being inspected.</summary>
    internal const string MoveWhileImpersonatingMessage = "Alerts can't be moved while inspecting another account.";

    /// <summary>
    /// A move writes the other human's alarms, so it is never available while someone else's account is being inspected.
    /// Null when the caller may move. <c>POST /api/alarm-move</c> and a <c>PUT /{uid}</c> naming the other destination share it.
    /// </summary>
    protected IActionResult? RefuseMoveWhileImpersonating() => this.IsImpersonating
        ? this.StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = MoveWhileImpersonatingMessage
        })
        : null;

    /// <summary>
    /// The answer to an update that asked to move its alarm to the other side of a linked pair. It keeps PUT's
    /// normal shape: the alarm as it now stands on the target, with <c>X-Move-Status</c> saying whether the
    /// original is gone (<c>moved</c>) or still there (<c>source_remains</c>). A collision is rethrown so the
    /// global filter answers it as it answers any other edit (409 with <c>existingUid</c>); a refused preflight
    /// is 409 <c>{ error }</c>, and a create PoracleNG did not explain is 502.
    /// </summary>
    protected IActionResult MovedResponse<TModel>(MoveResult<TModel> result) where TModel : class
    {
        switch (result.Outcome.Status)
        {
            case MoveStatus.Conflict:
                throw result.Conflict!;
            // An edit only moves to the side the alarm is not on, so AlreadyThere can't arrive here; were it to, it is
            // refused rather than answered as a move that never happened.
            case MoveStatus.Refused:
            case MoveStatus.AlreadyThere:
                return this.StatusCode(StatusCodes.Status409Conflict, new
                {
                    error = result.Outcome.Reason,
                    code = result.Outcome.ReasonCode,
                    app = result.Outcome.ReasonApp,
                    place = result.Outcome.ReasonPlace,
                });
            case MoveStatus.Failed:
                return this.StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = result.Outcome.Reason,
                    code = result.Outcome.ReasonCode,
                    app = result.Outcome.ReasonApp,
                });
            default:
                this.Response.Headers["X-Move-Status"] = result.Outcome.Status == MoveStatus.Moved ? "moved" : "source_remains";
                return this.Ok(result.Created);
        }
    }
}
