using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public abstract class ControllerTestBase
{
    protected static void SetupUser(ControllerBase controller, string userId = "123456789", int profileNo = 1, bool isAdmin = false, string username = "TestUser", string[]? managedWebhooks = null)
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

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }
}
