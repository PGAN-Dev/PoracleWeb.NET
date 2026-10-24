using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public abstract class ControllerTestBase
{
    /// <summary>The router a caller with no link gets: every call passes straight through to the service.</summary>
    protected static Pgan.PoracleWebNet.Core.Services.LinkedAlarmRouter UnlinkedRouter()
    {
        var linked = new Moq.Mock<Pgan.PoracleWebNet.Core.Abstractions.Services.ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(Moq.It.IsAny<string>()))
            .Returns((string id) => Task.FromResult(Pgan.PoracleWebNet.Core.Models.LinkedPair.Single(id)));
        return new Pgan.PoracleWebNet.Core.Services.LinkedAlarmRouter(linked.Object, InertMover(linked.Object));
    }

    /// <summary>A mover whose collaborators are all stubs, for tests that never move anything.</summary>
    internal static Pgan.PoracleWebNet.Core.Services.AlarmMover InertMover(Pgan.PoracleWebNet.Core.Abstractions.Services.ILinkedAccount linked) => new(
        linked,
        Moq.Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IHumanService>(),
        Moq.Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IPoracleHumanProxy>(),
        Moq.Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IPoracleTrackingProxy>(),
        Moq.Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Services.IUserAreaPolicy>(),
        new Pgan.PoracleWebNet.Core.Services.QuickPickUidTransfer(
            Moq.Mock.Of<Pgan.PoracleWebNet.Core.Abstractions.Repositories.IQuickPickAppliedStateRepository>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Pgan.PoracleWebNet.Core.Services.QuickPickUidTransfer>.Instance),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Pgan.PoracleWebNet.Core.Services.AlarmMover>.Instance);

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
