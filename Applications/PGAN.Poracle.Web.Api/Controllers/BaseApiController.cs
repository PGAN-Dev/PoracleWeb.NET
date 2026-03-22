using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PGAN.Poracle.Web.Api.Controllers;

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
}
