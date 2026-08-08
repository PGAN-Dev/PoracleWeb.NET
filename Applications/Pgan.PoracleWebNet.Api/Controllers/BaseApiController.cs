using System.Globalization;
using System.Security.Claims;
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

    /// <summary>Checks that <paramref name="ownerId"/> matches the authenticated user. Returns true when NOT owned (i.e. should return 404).</summary>
    protected bool NotOwnedByCurrentUser(string? ownerId) => !string.Equals(ownerId, this.UserId, StringComparison.Ordinal);
}
