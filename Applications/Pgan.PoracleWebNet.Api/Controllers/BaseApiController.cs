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

    /// <summary>Checks that <paramref name="ownerId"/> matches the authenticated user. Returns true when NOT owned (i.e. should return 404).</summary>
    protected bool NotOwnedByCurrentUser(string? ownerId) => !string.Equals(ownerId, this.UserId, StringComparison.Ordinal);
}
