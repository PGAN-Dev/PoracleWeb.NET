using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class AccountLinkControllerTests
{
    private const string BotToken = "123456:test-bot-token";
    private const string DiscordId = "100000000000000001";
    private const string TelegramId = "200000001";

    private static readonly AccountLink Link = new(1, DiscordId, TelegramId, LinkSide.Discord, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);

    private readonly Mock<IAccountLinkService> _service = new();
    private readonly Mock<ILinkedAccount> _linked = new();
    private readonly Mock<IPairDriftService> _drift = new();
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Mock<IAccountLinkRepository> _repo = new();
    private readonly EphemeralDataProtectionProvider _protection = new();

    public AccountLinkControllerTests() =>
        this._drift.Setup(d => d.GetAsync(It.IsAny<LinkedPair>())).ReturnsAsync([]);

    private AccountLinkController Create(string userId = DiscordId, bool impersonating = false, bool telegramEnabled = true)
    {
        var sut = new AccountLinkController(
            this._service.Object,
            this._linked.Object,
            this._settings.Object,
            this._protection,
            Options.Create(new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(new TelegramSettings { Enabled = telegramEnabled, BotToken = BotToken }),
            new ConfigurationBuilder().Build(),
            this._drift.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountLinkController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("alerts.example.net");
        var claims = new List<Claim> { new("userId", userId) };
        if (impersonating)
        {
            claims.Add(new Claim("impersonatedBy", "admin-1"));
        }

        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        sut.ControllerContext = new ControllerContext { HttpContext = http };
        return sut;
    }

    private static Dictionary<string, string> Widget(DateTimeOffset authDate, string id = TelegramId)
    {
        var data = new Dictionary<string, string>
        {
            ["id"] = id,
            ["first_name"] = "Riverside",
            ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var check = string.Join("\n", data.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"));
        using var hmac = new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(BotToken)));
        data["hash"] = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(check)));
        return data;
    }

    private static string? Cookie(ControllerBase c, string name) =>
        c.Response.Headers.SetCookie.FirstOrDefault(x => x != null && x.StartsWith(name + "=", StringComparison.Ordinal));

    private static object? Prop(object? value, string name) => value!.GetType().GetProperty(name)!.GetValue(value);

    /// <summary>A 403 that says why, so the SPA has a sentence to show rather than an empty body.</summary>
    private static void AssertImpersonationRefusal(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(Prop(obj.Value, "error") as string));
    }

    [Fact]
    public async Task DiscordStart_SetsStateAndLinkCookies_AndUrlCarriesTheSameState()
    {
        var sut = this.Create();

        var result = Assert.IsType<OkObjectResult>(await sut.DiscordStart());

        var url = (string)Prop(result.Value, "url")!;
        var stateCookie = Cookie(sut, "oauth_state")!;
        var state = Uri.UnescapeDataString(stateCookie["oauth_state=".Length..].Split(';')[0]);
        Assert.Contains("state=" + Uri.EscapeDataString(state), url, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=" + Uri.EscapeDataString("https://alerts.example.net/api/auth/discord/callback"), url, StringComparison.Ordinal);
        var link = Cookie(sut, "oauth_link")!;
        Assert.NotNull(link);
        var sealedValue = Uri.UnescapeDataString(link["oauth_link=".Length..].Split(';')[0]);
        var ticket = Pgan.PoracleWebNet.Api.Services.AccountLinkTicket.Open(this._protection, sealedValue, DateTimeOffset.UtcNow);
        Assert.Equal(DiscordId, ticket!.HumanId);
        Assert.Equal(state, ticket.State);
        Assert.Contains("httponly", link, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", link, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", link, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscordStart_DiscordSignInDisabledBySetting_IsNotPermitted()
    {
        this._settings.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("false");

        var result = await this.Create().DiscordStart();

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task TelegramLink_FiveMinuteOldPayload_Links()
    {
        this._service.Setup(s => s.LinkAsync(DiscordId, LinkSide.Telegram, TelegramId, "Riverside")).ReturnsAsync(Link);

        var result = await this.Create().TelegramLink(Widget(DateTimeOffset.UtcNow.AddMinutes(-5)));

        Assert.IsType<OkObjectResult>(result);
        this._service.Verify(s => s.LinkAsync(DiscordId, LinkSide.Telegram, TelegramId, "Riverside"), Times.Once);
    }

    [Fact]
    public async Task TelegramLink_ElevenMinuteOldPayload_IsRefusedLinkExpired()
    {
        var result = await this.Create().TelegramLink(Widget(DateTimeOffset.UtcNow.AddMinutes(-11)));

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
        Assert.Equal("link_expired", Prop(obj.Value, "code"));
        this._service.Verify(s => s.LinkAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TelegramLink_BadHash_IsRefusedAndNothingLinked()
    {
        var data = Widget(DateTimeOffset.UtcNow);
        data["id"] = "200000999";

        var result = await this.Create().TelegramLink(data);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
        this._service.Verify(s => s.LinkAsync(It.IsAny<string>(), It.IsAny<LinkSide>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TelegramLink_TelegramSignInDisabledBySetting_IsNotPermitted()
    {
        this._settings.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync("false");

        var result = await this.Create().TelegramLink(Widget(DateTimeOffset.UtcNow));

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        Assert.Equal("link_not_permitted", Prop(obj.Value, "code"));
    }

    [Fact]
    public async Task EveryWrite_WhileImpersonating_Is403()
    {
        var sut = this.Create(impersonating: true);

        AssertImpersonationRefusal(await sut.DiscordStart());
        AssertImpersonationRefusal(await sut.TelegramLink(Widget(DateTimeOffset.UtcNow)));
        AssertImpersonationRefusal(await sut.Unlink());
        AssertImpersonationRefusal(await sut.SetDefault(new DefaultDestinationRequest("telegram")));
        this._service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Status_WhileImpersonating_IsStillAnswered()
    {
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(LinkedPair.Single(DiscordId));

        var result = await this.Create(impersonating: true).Status();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Unlink_RemovesTheCallersLink_AndAnswers204()
    {
        var result = await this.Create().Unlink();

        Assert.IsType<NoContentResult>(result);
        this._service.Verify(s => s.UnlinkAsync(DiscordId), Times.Once);
    }

    [Fact]
    public async Task SetDefault_ParsesTheSide_AndAnswers204()
    {
        var result = await this.Create().SetDefault(new DefaultDestinationRequest("telegram"));

        Assert.IsType<NoContentResult>(result);
        this._service.Verify(s => s.SetDefaultAsync(DiscordId, LinkSide.Telegram), Times.Once);
    }

    [Fact]
    public async Task SetDefault_UnknownSide_Is400()
    {
        var result = await this.Create().SetDefault(new DefaultDestinationRequest("signal"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Status_Unlinked_AnswersLinkedFalse()
    {
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(LinkedPair.Single(DiscordId));

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        Assert.Equal(false, Prop(ok.Value, "linked"));
    }

    [Fact]
    public async Task Status_Linked_ListsEachSideWithItsPauseReason()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, false, PauseReason.NotRegistered));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"linked\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"defaultDestination\":\"discord\"", json, StringComparison.Ordinal);
        Assert.Contains("\"side\":\"telegram\"", json, StringComparison.Ordinal);
        Assert.Contains("\"pause\":\"not_registered\"", json, StringComparison.Ordinal);
        Assert.Contains("\"exists\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_Linked_NamesEachSideFromItsHumanRow_AndNullForAMissingSide()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None, "Riverside"),
            new PairSide(LinkSide.Telegram, TelegramId, false, PauseReason.NotRegistered));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"side\":\"discord\",\"exists\":true,\"pause\":\"none\",\"name\":\"Riverside\"", json, StringComparison.Ordinal);
        Assert.Contains("\"side\":\"telegram\",\"exists\":false,\"pause\":\"not_registered\",\"name\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_Routes_StayOpenWithLinkingOff_SoAStuckPairCanBeCut()
    {
        Assert.Empty(typeof(AdminAccountLinkController).GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), true));
        // The person's own routes are still gated: switched off, nobody links or unlinks themselves.
        Assert.NotEmpty(typeof(AccountLinkController).GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), true));
    }

    [Theory]
    [InlineData(LinkRefusalCode.Taken, 409)]
    [InlineData(LinkRefusalCode.ProvisionFailed, 502)]
    [InlineData(LinkRefusalCode.NotPermitted, 403)]
    [InlineData(LinkRefusalCode.WrongType, 400)]
    [InlineData(LinkRefusalCode.Expired, 400)]
    public void RefusalFilter_MapsCodesToStatuses(LinkRefusalCode code, int status)
    {
        var ctx = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext(
            new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            [],
            new object())
        {
            Exception = new LinkRefusalException(code, "no"),
        };

        new LinkRefusalExceptionFilter().OnActionExecuted(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(status, obj.StatusCode);
        Assert.True(ctx.ExceptionHandled);
        Assert.Equal(LinkRefusalException.Wire(code), Prop(obj.Value, "code"));
    }

    [Fact]
    public async Task Status_Linked_CarriesDriftItems()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, true, PauseReason.None));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);
        this._drift.Setup(d => d.GetAsync(pair)).ReturnsAsync([new DriftItem(
            DriftField.CurrentProfile,
            System.Text.Json.JsonSerializer.SerializeToElement(1),
            System.Text.Json.JsonSerializer.SerializeToElement(2),
            "abc")]);

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"drift\":[{\"field\":\"current_profile\",\"discord\":1,\"telegram\":2,\"fingerprint\":\"abc\",\"details\":null}]", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_Linked_CarriesLegacyProfileZeroSide()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, true, PauseReason.None));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);
        this._drift.Setup(d => d.GetLegacyProfileZeroAsync(pair)).ReturnsAsync(LinkSide.Discord);

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"legacyProfileZero\":\"discord\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_Linked_LegacyProfileZeroNullWhenTheReadFails()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, true, PauseReason.None));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);
        this._drift.Setup(d => d.GetLegacyProfileZeroAsync(pair)).ThrowsAsync(new HttpRequestException("down"));

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"legacyProfileZero\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_Unlinked_DoesNotAskForDrift()
    {
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(LinkedPair.Single(DiscordId));

        await this.Create().Status();

        this._drift.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resolve_Valid_Answers204()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, true, PauseReason.None));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);

        var result = await this.Create().Resolve(new ResolveDriftRequest("location", "telegram", "abc"));

        Assert.IsType<NoContentResult>(result);
        this._drift.Verify(d => d.ResolveAsync(pair, DriftField.Location, LinkSide.Telegram, "abc"), Times.Once);
    }

    [Fact]
    public async Task Resolve_WhileImpersonating_IsForbidden()
    {
        var result = await this.Create(impersonating: true).Resolve(new ResolveDriftRequest("location", "telegram", "abc"));

        AssertImpersonationRefusal(result);
        this._drift.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("nonsense", "telegram", "abc")]
    [InlineData("location", "nonsense", "abc")]
    [InlineData("location", "telegram", "")]
    public async Task Resolve_BadBody_Answers400(string field, string keep, string expected)
    {
        var result = await this.Create().Resolve(new ResolveDriftRequest(field, keep, expected));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Status_DriftReadFails_AnswersTheRestWithDriftNull()
    {
        var pair = new LinkedPair(DiscordId, Link,
            new PairSide(LinkSide.Discord, DiscordId, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, TelegramId, true, PauseReason.None));
        this._linked.Setup(l => l.ResolveAsync(DiscordId)).ReturnsAsync(pair);
        this._drift.Setup(d => d.GetAsync(pair)).ThrowsAsync(new HttpRequestException("poracle is down"));

        var ok = Assert.IsType<OkObjectResult>(await this.Create().Status());

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"linked\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"drift\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"side\":\"discord\"", json, StringComparison.Ordinal);
    }

    private static Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext ContextFor(Exception ex) => new(
        new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
        [],
        new object())
    {
        Exception = ex,
    };

    [Fact]
    public void DriftFilter_PartialAndPlaceInUse_AreAlso409WithTheirText()
    {
        var partial = ContextFor(new DriftPartiallyAppliedException(new InvalidOperationException("inner")));
        var inUse = ContextFor(new DriftPlaceInUseException(LinkSide.Telegram, "home", 2));

        new PairDriftExceptionFilter().OnActionExecuted(partial);
        new PairDriftExceptionFilter().OnActionExecuted(inUse);

        Assert.Equal(409, Assert.IsType<ObjectResult>(partial.Result).StatusCode);
        Assert.Equal("partially_applied", Prop(((ObjectResult)partial.Result!).Value, "code"));
        Assert.Contains("Linked accounts", (string)Prop(((ObjectResult)partial.Result!).Value, "error")!, StringComparison.Ordinal);
        Assert.Equal(409, Assert.IsType<ObjectResult>(inUse.Result).StatusCode);
        var body = ((ObjectResult)inUse.Result!).Value;
        Assert.Equal("place_in_use", Prop(body, "code"));
        Assert.Equal("telegram", Prop(body, "side"));
        Assert.Equal("home", Prop(body, "name"));
        Assert.Equal(2, Prop(body, "count"));
        Assert.Contains("Telegram's saved place \"home\"", (string)Prop(body, "error")!, StringComparison.Ordinal);
    }

    [Fact]
    public void DriftFilter_ProfileMissing_NamesTheAppAndTheProfile()
    {
        var missing = ContextFor(new DriftProfileMissingException(LinkSide.Discord, 2, "Weekend"));

        new PairDriftExceptionFilter().OnActionExecuted(missing);

        var body = ((ObjectResult)missing.Result!).Value;
        Assert.Equal(409, ((ObjectResult)missing.Result!).StatusCode);
        Assert.Equal("profile_missing", Prop(body, "code"));
        Assert.Equal("discord", Prop(body, "side"));
        Assert.Equal("Weekend", Prop(body, "name"));
        Assert.DoesNotContain("not found", (string)Prop(body, "error")!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriftFilter_LeavesOtherExceptionsAlone()
    {
        var other = ContextFor(new InvalidOperationException("x"));

        new PairDriftExceptionFilter().OnActionExecuted(other);

        Assert.Null(other.Result);
        Assert.False(other.ExceptionHandled);
    }

    [Fact]
    public void DriftFilter_ChangedAndHoldsAlarms_CarryTheirTextInTheBody()
    {
        var changed = ContextFor(new DriftChangedException());
        var holds = ContextFor(new DriftProfileHoldsAlarmsException(LinkSide.Telegram, 2, "Spare", 3));

        new PairDriftExceptionFilter().OnActionExecuted(changed);
        new PairDriftExceptionFilter().OnActionExecuted(holds);

        Assert.Equal("drift_changed", Prop(((ObjectResult)changed.Result!).Value, "code"));
        var body = ((ObjectResult)holds.Result!).Value;
        Assert.Equal("profile_holds_alarms", Prop(body, "code"));
        Assert.Equal("telegram", Prop(body, "side"));
        Assert.Equal("Spare", Prop(body, "name"));
        Assert.Equal(3, Prop(body, "count"));
        // Names the app, not "the other account".
        Assert.Contains("Telegram's profile Spare has 3 alarm(s)", (string)Prop(body, "error")!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(400)]
    public void RefusedFilter_PassesTheResolveRefusalStatusThrough(int status)
    {
        var ctx = ContextFor(new PoracleRequestRefusedException("reason", status) { StatusCode = status });

        new PoracleRequestRefusedExceptionFilter().OnActionExecuted(ctx);

        Assert.Equal(status, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
        Assert.Equal("reason", Prop(((ObjectResult)ctx.Result!).Value, "error"));
    }

    [Fact]
    public void DriftFilter_MapsBothExceptionsTo409()
    {
        foreach (Exception ex in new Exception[] { new DriftChangedException(), new DriftProfileHoldsAlarmsException(LinkSide.Telegram, 2, "Spare", 3) })
        {
            var ctx = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
                [],
                new object())
            {
                Exception = ex,
            };

            new PairDriftExceptionFilter().OnActionExecuted(ctx);

            Assert.Equal(409, Assert.IsType<ObjectResult>(ctx.Result).StatusCode);
            Assert.Equal(ex.Message, Prop(((ObjectResult)ctx.Result!).Value, "error"));
            Assert.True(ctx.ExceptionHandled);
        }
    }

    [Fact]
    public async Task Admin_List_AsAdmin_ReturnsLinks()
    {
        this._repo.Setup(r => r.GetAllAsync()).ReturnsAsync([Link]);

        var result = await this.CreateAdmin(admin: true).List();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Admin_Unlink_AsAdmin_Answers204()
    {
        var result = await this.CreateAdmin(admin: true).Unlink(DiscordId);

        Assert.IsType<NoContentResult>(result);
        this._service.Verify(s => s.UnlinkAsync(DiscordId), Times.Once);
    }

    [Fact]
    public async Task Admin_Unlink_ByNonAdmin_Is403()
    {
        var result = await this.CreateAdmin(admin: false).Unlink(DiscordId);

        Assert.IsType<ForbidResult>(result);
        this._service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Admin_Unlink_WhileImpersonating_Is403()
    {
        var result = await this.CreateAdmin(admin: true, impersonating: true).Unlink(DiscordId);

        AssertImpersonationRefusal(result);
        this._service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Admin_List_ByNonAdmin_Is403() =>
        Assert.IsType<ForbidResult>(await this.CreateAdmin(admin: false).List());

    private AdminAccountLinkController CreateAdmin(bool admin, bool impersonating = false)
    {
        var sut = new AdminAccountLinkController(this._repo.Object, this._service.Object);
        var claims = new List<Claim> { new("userId", "admin-1"), new("isAdmin", admin ? "true" : "false") };
        if (impersonating)
        {
            claims.Add(new Claim("impersonatedBy", "admin-0"));
        }

        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
        return sut;
    }
}
