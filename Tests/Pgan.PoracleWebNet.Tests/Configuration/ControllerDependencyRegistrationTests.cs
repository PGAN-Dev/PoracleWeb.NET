using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Every controller must be constructible from the container the application actually builds.
/// </summary>
/// <remarks>
/// <para>
/// <c>PokestopEventController</c> shipped depending on an <c>IPokestopEventService</c> that
/// <see cref="ServiceCollectionExtensions.AddPoracleServices"/> never registered. Every endpoint on it
/// answered 500 from the moment it merged. Nothing caught it: the unit tests construct the service
/// directly, the controller tests mock it, the solution compiles, and CI is green -- a missing
/// registration is invisible until something resolves the controller, which only happens when the
/// application runs.
/// </para>
/// <para>
/// So this asserts the one thing those tests cannot: that the real registration method can supply every
/// constructor argument of every controller in the API assembly. It fails when a service is added
/// without its registration, which is the mistake, rather than when a name is misspelled, which the
/// compiler already catches.
/// </para>
/// </remarks>
public class ControllerDependencyRegistrationTests
{
    /// <summary>
    /// Supplied by the host rather than by <c>AddPoracleServices</c>, so their absence from the
    /// application's own registrations is correct.
    /// </summary>
    private static readonly HashSet<string> HostProvided =
    [
        "IConfiguration",
        "IHostEnvironment",
        "IWebHostEnvironment",
        "ILogger`1",
        "IHttpClientFactory",
        "IMemoryCache",
        "IServiceProvider",
        "IHttpContextAccessor",
    ];

    public static TheoryData<Type> Controllers()
    {
        var data = new TheoryData<Type>();

        foreach (var controller in typeof(ServiceCollectionExtensions).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name))
        {
            data.Add(controller);
        }

        return data;
    }

    [Fact]
    public void TheAssemblyActuallyHasControllersToCheck()
    {
        // A reflection filter that silently matches nothing would make every case below vacuous.
        Assert.True(Controllers().Count > 15);
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void EveryControllerDependencyIsRegistered(Type controller)
    {
        var registered = BuildRegistrations();

        var missing = controller
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            // A parameter with a default is the graceful-degradation pattern: the scanner DB and the
            // Golbat API are both optional, their controllers take `IThing? thing = null`, and the
            // container is expected to leave them null. Only a REQUIRED dependency has to be registered.
            .Where(p => !p.HasDefaultValue)
            .Select(p => p.ParameterType)
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
            .Where(t => t.IsInterface)
            .Select(t => t.Name)
            .Where(name => !HostProvided.Contains(name) && !registered.Contains(name))
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{controller.Name} injects {string.Join(", ", missing)}, which AddPoracleServices does not "
            + "register. Every request to it would fail to resolve and answer 500.");
    }

    private static HashSet<string> BuildRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPoracleServices(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = "http://localhost:3030",
                ["Poracle:ApiSecret"] = "test-secret",
                ["Jwt:Secret"] = "test-secret-that-is-long-enough-for-hmac-sha256-signing",
                ["ConnectionStrings:PoracleDb"] = "server=localhost;database=poracle;user=root;password=x",
                ["ConnectionStrings:PoracleWebDb"] = "server=localhost;database=poracle_web;user=root;password=x",
            })
            .Build());

        return services
            .Select(d => d.ServiceType.IsGenericType
                ? d.ServiceType.GetGenericTypeDefinition().Name
                : d.ServiceType.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
