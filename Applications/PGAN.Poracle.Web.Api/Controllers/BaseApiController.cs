using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PGAN.Poracle.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    protected string UserId => User.FindFirstValue("userId") ?? throw new UnauthorizedAccessException();
    protected int ProfileNo => int.Parse(User.FindFirstValue("profileNo") ?? "1");
    protected bool IsAdmin => User.FindFirstValue("isAdmin") == "true";
    protected string Username => User.FindFirstValue("username") ?? string.Empty;
    protected string[] ManagedWebhooks => User.FindFirstValue("managedWebhooks")
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
