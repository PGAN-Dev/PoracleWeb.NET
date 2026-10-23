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
    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Mock<IAccountLinkRepository> _repo = new();
    private readonly EphemeralDataProtectionProvider _protection = new();

    private AccountLinkController Create(string userId = DiscordId, bool impersonating = false, bool telegramEnabled = true)
    {
        var sut = new AccountLinkController(
            this._service.Object,
            this._linked.Object,
            this._settings.Object,
            this._protection,
            Options.Create(new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(new TelegramSettings { Enabled = telegramEnabled, BotToken = BotToken }),
            new ConfigurationBuilder().Build());
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

        Assert.IsType<ForbidResult>(await sut.DiscordStart());
        Assert.IsType<ForbidResult>(await sut.TelegramLink(Widget(DateTimeOffset.UtcNow)));
        Assert.IsType<ForbidResult>(await sut.Unlink());
        Assert.IsType<ForbidResult>(await sut.SetDefault(new DefaultDestinationRequest("telegram")));
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

        Assert.IsType<ForbidResult>(result);
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
