using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public abstract class ControllerTestBase
{
    protected static void SetupUser(ControllerBase controller, string userId = "123456789", int profileNo = 1, bool isAdmin = false, string username = "TestUser", string[]? managedWebhooks = null, string? impersonatedBy = null)
    {
        var claims = new List<Claim>
        {
            new("userId", userId),
            new("profileNo", profileNo.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("isAdmin", isAdmin.ToString().ToLowerInvariant()),
            new("username", username),
        };

        if (managedWebhooks is { Length: > 0 })
        {
            claims.Add(new Claim("managedWebhooks", string.Join(',', managedWebhooks)));
        }

        if (impersonatedBy is not null)
        {
            claims.Add(new Claim("impersonatedBy", impersonatedBy));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    /// <summary>A session inspecting somebody else's account: <c>userId</c> names the account being looked at.</summary>
    protected static void SetupImpersonatingUser(ControllerBase controller, string userId = "123456789", int profileNo = 1,
        bool isAdmin = false, string impersonatedBy = "admin-1") =>
        SetupUser(controller, userId, profileNo, isAdmin, impersonatedBy: impersonatedBy);
}
