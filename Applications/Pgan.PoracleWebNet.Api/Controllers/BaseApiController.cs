using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    protected string[] ManagedWebhooks => this.User.FindFirstValue("managedWebhooks")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>
    /// Rejects a distance the create path would have rejected. The "update all" endpoints bind a bare
    /// <c>[FromBody] int</c>, which model validation cannot annotate, so the check has to be explicit.
    /// Returns <c>null</c> when the value is acceptable. See #417.
    /// </summary>
    protected IActionResult? RejectInvalidDistance(int distance) =>
        distance < 0
            ? this.BadRequest(new
            {
                error = "Distance must be zero or greater."
            })
            : null;

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
}
