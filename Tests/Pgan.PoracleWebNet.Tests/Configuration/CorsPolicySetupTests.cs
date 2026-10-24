using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

public class CorsPolicySetupTests
{
    private static CorsResult Evaluate(string[]? allowed, string origin)
    {
        var builder = new CorsPolicyBuilder();
        CorsPolicySetup.Apply(builder, allowed);
        var service = new CorsService(Options.Create(new CorsOptions()), NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = origin;
        return service.EvaluatePolicy(context, builder.Build());
    }

    [Fact]
    public void AnAllowedOriginSeesTheMoveAndPairHeaders()
    {
        var result = Evaluate(["https://alerts.example"], "https://alerts.example");

        Assert.True(result.IsOriginAllowed);
        Assert.Contains("X-Move-Status", result.AllowedExposedHeaders);
        Assert.Contains("X-Pair-Warning", result.AllowedExposedHeaders);
    }

    [Fact]
    public void AnOriginOutsideTheListIsStillRefused()
    {
        var result = Evaluate(["https://alerts.example"], "https://elsewhere.example");

        Assert.False(result.IsOriginAllowed);
    }
}
