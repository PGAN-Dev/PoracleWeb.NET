using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class VersionControllerTests
{
    private const string Sha = "3f8d38aa4724209ec7ebaf4f0a1053d063008734";

    [Fact]
    public void ReturnsBuildMetadataFromConfiguration()
    {
        var sut = Build(new()
        {
            ["BUILD_VERSION"] = "beta",
            ["BUILD_REVISION"] = Sha,
            ["BUILD_DATE"] = "2026-08-05T14:22:16Z",
        });

        var payload = Payload(sut.Get());

        Assert.Equal("beta", Read(payload, "version"));
        Assert.Equal(Sha, Read(payload, "revision"));
        Assert.Equal("2026-08-05T14:22:16Z", Read(payload, "buildDate"));
        Assert.Equal("Production", Read(payload, "environment"));
    }

    [Fact]
    public void ShortensRevisionToSevenCharacters()
    {
        var sut = Build(new() { ["BUILD_REVISION"] = Sha });

        Assert.Equal("3f8d38a", Read(Payload(sut.Get()), "revisionShort"));
    }

    [Fact]
    public void FallsBackToUnknownWhenBuildArgsWereNotSupplied()
    {
        // A local `docker build` or `dotnet run` passes no build args at all.
        var payload = Payload(Build([]).Get());

        Assert.Equal(VersionController.Unknown, Read(payload, "version"));
        Assert.Equal(VersionController.Unknown, Read(payload, "revision"));
        Assert.Equal(VersionController.Unknown, Read(payload, "revisionShort"));
        Assert.Equal(VersionController.Unknown, Read(payload, "buildDate"));
    }

    [Fact]
    public void TreatsBlankValuesAsUnknown()
    {
        // An unset build arg reaches the container as an empty string, not a missing key.
        var payload = Payload(Build(new() { ["BUILD_VERSION"] = "", ["BUILD_REVISION"] = "   " }).Get());

        Assert.Equal(VersionController.Unknown, Read(payload, "version"));
        Assert.Equal(VersionController.Unknown, Read(payload, "revision"));
    }

    [Fact]
    public void DoesNotTruncateARevisionShorterThanSevenCharacters()
    {
        var payload = Payload(Build(new() { ["BUILD_REVISION"] = "abc" }).Get());

        Assert.Equal("abc", Read(payload, "revisionShort"));
    }

    private static VersionController Build(Dictionary<string, string?> values)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Production");

        return new VersionController(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            environment.Object);
    }

    private static object Payload(IActionResult result) => Assert.IsType<OkObjectResult>(result).Value!;

    private static string Read(object payload, string property) =>
        payload.GetType().GetProperty(property)!.GetValue(payload)!.ToString()!;
}
