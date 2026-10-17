using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// Reports which build is actually running. The image already carries this in its OCI labels,
/// but those are only readable via `docker inspect` on the host -- which is no help when you
/// want to know what a deployed instance is serving from outside, or when the image was built
/// locally and carries no labels at all.
/// </summary>
[ApiController]
[Route("api/version")]
public class VersionController(IConfiguration configuration, IHostEnvironment environment) : ControllerBase
{
    /// <summary>Fallback when the build args were not supplied (local `docker build`, `dotnet run`).</summary>
    internal const string Unknown = "unknown";

    /// <summary>Channel of an image built without <c>RELEASE_CHANNEL</c>: every official release and branch build.</summary>
    internal const string Official = "official";

    private readonly IConfiguration _configuration = configuration;
    private readonly IHostEnvironment _environment = environment;

    /// <summary>
    /// Returns the running build's version, git revision and build timestamp.
    /// </summary>
    /// <remarks>
    /// Anonymous on purpose: the main use is checking a deployment from outside without
    /// credentials. Nothing here is sensitive -- the repository is public, so the commit SHA
    /// is already visible on GitHub, and no configuration or secret is exposed.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        var revision = Value("BUILD_REVISION");

        return this.Ok(new
        {
            version = Value("BUILD_VERSION"),
            revision,
            // Short form purely for convenience -- it is what you actually paste into `git log`.
            revisionShort = revision == Unknown ? Unknown : revision[..Math.Min(7, revision.Length)],
            buildDate = Value("BUILD_DATE"),
            // "official" unless the image was built with RELEASE_CHANNEL (the experimental line sets it).
            // Blank is official rather than unknown: an unset build arg is the normal case, not a gap.
            channel = Value("RELEASE_CHANNEL", Official),
            environment = this._environment.EnvironmentName,
        });
    }

    private string Value(string key, string fallback = Unknown)
    {
        var value = this._configuration[key];

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
