using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// The role check walks every guild in <c>DISCORD_GUILD_ID</c>. The responses are the bodies Discord
/// sends for <c>GET /guilds/{guild}/members/{user}</c>, recorded on 2026-11-02: a user who isn't in a
/// guild gets 10007 Unknown Member, and a guild that doesn't exist or doesn't have the bot gets
/// 10004 Unknown Guild. Both are a 404.
/// </summary>
public class AuthControllerGuildRoleCheckTests
{
    private const string DiscordId = "100000000000000001";
    private const string AllowedRole = "300000000000000001";
    private const string OtherRole = "300000000000000002";
    private const string GuildA = "400000000000000001";
    private const string GuildB = "400000000000000002";

    private const string UnknownMember = /*lang=json,strict*/ """{"message": "Unknown Member", "code": 10007}""";
    private const string UnknownGuild = /*lang=json,strict*/ """{"message": "Unknown Guild", "code": 10004}""";
    private const string MissingAccess = /*lang=json,strict*/ """{"message": "Missing Access", "code": 50001}""";

    private readonly CapturingLogger<AuthController> _logger = new();

    private static (HttpStatusCode Status, string Body) Member(params string[] roles) =>
        (HttpStatusCode.OK, $$"""{"roles": [{{string.Join(", ", roles.Select(r => $"\"{r}\""))}}]}""");

    private sealed class StubGuilds(Dictionary<string, (HttpStatusCode Status, string Body)> byGuild) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        public HashSet<string> Unreachable { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var guild = request.RequestUri!.Segments[^3].TrimEnd('/');
            this.Asked.Add(guild);
            if (this.Unreachable.Contains(guild))
            {
                throw new HttpRequestException("connection reset");
            }

            var (status, body) = byGuild[guild];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private AuthController Create(string guildIds, HttpMessageHandler discord)
    {
        var settings = new Mock<ISiteSettingService>();
        settings.Setup(s => s.GetBoolAsync("enable_roles")).ReturnsAsync(true);
        settings.Setup(s => s.GetValueAsync("allowed_role_ids")).ReturnsAsync(AllowedRole);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(discord, false));

        return new AuthController(
            new Mock<IHumanService>().Object,
            new Mock<IProfileService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            PairTestSupport.UnlinkedWriter(),
            settings.Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            new Mock<IUserRoleResolver>().Object,
            new Mock<IOidcClient>().Object,
            new Mock<IOidcSessionService>().Object,
            Options.Create(new DiscordSettings { BotToken = "bot-token", GuildId = guildIds }),
            Options.Create(new TelegramSettings()),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().Build(),
            new Mock<IAccountLinkService>().Object,
            new EphemeralDataProtectionProvider(),
            factory.Object,
            this._logger);
    }

    private IEnumerable<string> Warnings => this._logger.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message);

    [Fact]
    public async Task SingleGuildWithTheRoleGrantsAccess()
    {
        var discord = new StubGuilds(new() { [GuildA] = Member(OtherRole, AllowedRole) });

        Assert.Null(await this.Create(GuildA, discord).CheckRoleAccessAsync(DiscordId));
    }

    [Fact]
    public async Task SingleGuildWithoutTheRoleIsMissingTheRole()
    {
        var discord = new StubGuilds(new() { [GuildA] = Member(OtherRole) });

        Assert.Equal("missing_required_role", await this.Create(GuildA, discord).CheckRoleAccessAsync(DiscordId));
    }

    [Fact]
    public async Task SingleGuildTheUserIsNotInIsNotInGuild()
    {
        var discord = new StubGuilds(new() { [GuildA] = (HttpStatusCode.NotFound, UnknownMember) });

        Assert.Equal("not_in_guild", await this.Create(GuildA, discord).CheckRoleAccessAsync(DiscordId));
        Assert.Empty(this.Warnings);
    }

    // The case several guilds exist for: a member only of the second guild is let in through it.
    [Fact]
    public async Task RoleInALaterGuildGrantsAccess()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = (HttpStatusCode.NotFound, UnknownMember),
            [GuildB] = Member(AllowedRole),
        });

        Assert.Null(await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
        Assert.Equal([GuildA, GuildB], discord.Asked);
        Assert.Empty(this.Warnings);
    }

    [Fact]
    public async Task StopsAtTheFirstGuildThatGrantsAccess()
    {
        var discord = new StubGuilds(new() { [GuildA] = Member(AllowedRole), [GuildB] = Member(AllowedRole) });

        Assert.Null(await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
        Assert.Equal([GuildA], discord.Asked);
    }

    [Fact]
    public async Task MemberOfOneGuildWithoutTheRoleIsMissingTheRole()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = Member(OtherRole),
            [GuildB] = (HttpStatusCode.NotFound, UnknownMember),
        });

        Assert.Equal("missing_required_role", await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
    }

    [Fact]
    public async Task InNoGuildIsNotInGuild()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = (HttpStatusCode.NotFound, UnknownMember),
            [GuildB] = (HttpStatusCode.NotFound, UnknownMember),
        });

        Assert.Equal("not_in_guild", await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
    }

    // A guild the bot can't read doesn't lock out someone who qualifies through another one...
    [Fact]
    public async Task UnreadableGuildDoesNotBlockAGrantFromAnother()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = (HttpStatusCode.Forbidden, MissingAccess),
            [GuildB] = Member(AllowedRole),
        });

        Assert.Null(await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
    }

    // ...but with no grant it fails the check, because the role might have been there.
    [Fact]
    public async Task UnreadableGuildWithNoGrantFailsTheCheck()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = (HttpStatusCode.Forbidden, MissingAccess),
            [GuildB] = Member(OtherRole),
        });

        Assert.Equal("role_check_failed", await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
        Assert.Contains(this.Warnings, w => w.Contains(GuildA, StringComparison.Ordinal));
    }

    // A request that fails outright (reset, DNS, timeout) is the same as an unreadable guild: it must
    // not end the check while another guild could still let the user in.
    [Fact]
    public async Task UnreachableGuildDoesNotBlockAGrantFromAnother()
    {
        var discord = new StubGuilds(new() { [GuildB] = Member(AllowedRole) }) { Unreachable = { GuildA } };

        Assert.Null(await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
        Assert.Equal([GuildA, GuildB], discord.Asked);
        Assert.Contains(this.Warnings, w => w.Contains(GuildA, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreachableGuildWithNoGrantFailsTheCheck()
    {
        var discord = new StubGuilds(new() { [GuildB] = (HttpStatusCode.NotFound, UnknownMember) }) { Unreachable = { GuildA } };

        Assert.Equal("role_check_failed", await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
    }

    // A mistyped guild ID, or one the bot was removed from, is a 404 like "not a member", so it used
    // to be logged at Debug and never seen. It names the guild at Warning now.
    [Fact]
    public async Task UnknownGuildIsLoggedAsAWarningNamingTheGuild()
    {
        var discord = new StubGuilds(new()
        {
            [GuildA] = (HttpStatusCode.NotFound, UnknownGuild),
            [GuildB] = Member(AllowedRole),
        });

        Assert.Null(await this.Create($"{GuildA},{GuildB}", discord).CheckRoleAccessAsync(DiscordId));
        var warning = Assert.Single(this.Warnings);
        Assert.Contains(GuildA, warning, StringComparison.Ordinal);
        Assert.Contains("DISCORD_GUILD_ID", warning, StringComparison.Ordinal);
    }

    // The denial itself doesn't change: a single bad guild still answers not_in_guild, as it always has.
    [Fact]
    public async Task UnknownGuildKeepsTheDenialReason()
    {
        var discord = new StubGuilds(new() { [GuildA] = (HttpStatusCode.NotFound, UnknownGuild) });

        Assert.Equal("not_in_guild", await this.Create(GuildA, discord).CheckRoleAccessAsync(DiscordId));
    }

    [Theory]
    [InlineData(UnknownGuild, true)]
    [InlineData(UnknownMember, false)]
    [InlineData(/*lang=json,strict*/ """{"message": "Unknown User", "code": 10013}""", false)]
    [InlineData("<html>not json</html>", false)]
    [InlineData("", false)]
    [InlineData(/*lang=json,strict*/ """{"code": "10004"}""", false)]
    public void RecognisesDiscordsUnknownGuildError(string body, bool expected) =>
        Assert.Equal(expected, AuthController.IsUnknownGuildError(body));
}
