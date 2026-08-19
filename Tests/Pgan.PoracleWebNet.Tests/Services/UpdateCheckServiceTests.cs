using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Telling an admin they are behind, without telling them so when they are not.
/// </summary>
/// <remarks>
/// The two projects publish differently and are read differently: PoracleWeb cuts GitHub releases, and
/// PoracleNG has neither releases nor tags, so its released number is the constant in
/// <c>processor/version.go</c> on main. That file is also what identifies a development build, since
/// develop carries the next version before it ships.
/// </remarks>
public class UpdateCheckServiceTests
{
    private const string ReleaseJson = """{"tag_name":"v2.16.0","name":"v2.16.0"}""";
    private const string VersionGo =
        """
        // Package processor exposes the PoracleNG processor version.
        package processor

        // Version is the PoracleNG processor version. Bump on each release.
        const Version = "5.1.0"
        """;

    private readonly Mock<ISiteSettingService> _siteSettings = new();

    private UpdateCheckService Service(
        string releaseBody = ReleaseJson,
        string versionBody = VersionGo,
        bool disabled = false,
        Exception? throws = null)
    {
        this._siteSettings.Setup(s => s.GetBoolAsync(UpdateCheckService.DisableKey)).ReturnsAsync(disabled);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                if (throws is not null)
                {
                    throw throws;
                }

                var body = request.RequestUri!.Host.Contains("raw.githubusercontent", StringComparison.Ordinal)
                    ? versionBody
                    : releaseBody;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            });

        return new UpdateCheckService(
            new HttpClient(handler.Object),
            this._siteSettings.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<UpdateCheckService>.Instance);
    }

    [Fact]
    public async Task SaysWhenBothAreBehind()
    {
        var (web, ng) = await this.Service().CheckAsync("2.15.3", "5.0.4");

        Assert.Equal(UpdateState.Behind, web.State);
        Assert.Equal("v2.16.0", web.Latest);
        Assert.Equal(UpdateState.Behind, ng.State);
        Assert.Equal("5.1.0", ng.Latest);
    }

    [Fact]
    public async Task SaysNothingIsDueWhenBothMatch()
    {
        // The legitimate twin. A banner that appears on an up-to-date deployment is a banner nobody
        // reads by the second week.
        var (web, ng) = await this.Service().CheckAsync("2.16.0", "5.1.0");

        Assert.Equal(UpdateState.UpToDate, web.State);
        Assert.Equal(UpdateState.UpToDate, ng.State);
    }

    [Fact]
    public async Task AVersionAheadOfTheReleaseIsADevelopmentBuild()
    {
        // PoracleNG's develop carries 5.2.0 while main still reads 5.1.0, so this is how a develop build
        // gives itself away -- the branch name never leaves the binary.
        var (_, ng) = await this.Service().CheckAsync("2.16.0", "5.2.0");

        Assert.Equal(UpdateState.PreRelease, ng.State);
    }

    [Fact]
    public async Task PoracleWebsBetaChannelIsNotComparedToAReleaseNumber()
    {
        // Dev runs an image tagged beta, which is not a point on the release line.
        var (web, _) = await this.Service().CheckAsync("beta", "5.1.0");

        Assert.Equal(UpdateState.Unknown, web.State);
    }

    [Fact]
    public async Task ALocalBuildIsNotReportedAsBehind()
    {
        var (web, ng) = await this.Service().CheckAsync("unknown", "0.0.0");

        Assert.Equal(UpdateState.Unknown, web.State);
        Assert.Equal(UpdateState.Unknown, ng.State);
    }

    [Fact]
    public async Task NothingLeavesTheDeploymentWhenTheCheckIsSwitchedOff()
    {
        var handlerCalls = 0;
        this._siteSettings.Setup(s => s.GetBoolAsync(UpdateCheckService.DisableKey)).ReturnsAsync(true);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(() =>
            {
                handlerCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            });

        var service = new UpdateCheckService(
            new HttpClient(handler.Object),
            this._siteSettings.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<UpdateCheckService>.Instance);

        var (web, ng) = await service.CheckAsync("2.15.3", "5.0.4");

        Assert.Equal(0, handlerCalls);
        Assert.Equal(UpdateState.Unknown, web.State);
        Assert.Equal(UpdateState.Unknown, ng.State);
    }

    [Fact]
    public async Task GitHubBeingUnreachableIsNotNews()
    {
        var (web, ng) = await this.Service(throws: new HttpRequestException("no route")).CheckAsync("2.15.3", "5.0.4");

        Assert.Equal(UpdateState.Unknown, web.State);
        Assert.Equal(UpdateState.Unknown, ng.State);
    }

    [Fact]
    public async Task AVersionFileItCannotParseSaysNothing()
    {
        var (_, ng) = await this.Service(versionBody: "package processor // nothing here").CheckAsync("2.16.0", "5.1.0");

        Assert.Equal(UpdateState.Unknown, ng.State);
        Assert.Null(ng.Latest);
    }

    [Fact]
    public async Task OneProjectFailingDoesNotHideTheOther()
    {
        var (web, ng) = await this.Service(releaseBody: "not json at all").CheckAsync("2.15.3", "5.0.4");

        Assert.Equal(UpdateState.Unknown, web.State);
        Assert.Equal(UpdateState.Behind, ng.State);
    }

    [Theory]
    [InlineData("v2.16.0", "2.16.0", UpdateState.UpToDate)]
    [InlineData("2.16.0", "v2.16.0", UpdateState.UpToDate)]
    public void ALeadingVeeIsNotAVersionDifference(string running, string latest, UpdateState expected)
    {
        // PoracleWeb tags releases as v2.16.0 and reports itself as 2.16.0.
        Assert.Equal(expected, UpdateStatus.Compare(running, latest).State);
    }

    [Fact]
    public async Task TheAnswerIsCachedRatherThanAskedPerPageLoad()
    {
        var service = this.Service();

        await service.CheckAsync("2.15.3", "5.1.0");
        await service.CheckAsync("2.15.3", "5.1.0");

        this._siteSettings.Verify(s => s.GetBoolAsync(UpdateCheckService.DisableKey), Times.Exactly(2));
    }
}
