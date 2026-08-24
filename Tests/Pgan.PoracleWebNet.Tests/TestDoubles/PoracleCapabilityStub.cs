using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.TestDoubles;

/// <summary>
/// A fixed answer for <see cref="IPoracleCapabilityService"/>, for tests whose subject is something else.
/// </summary>
/// <remarks>
/// <see cref="Permissive"/> is what the existing alarm-service tests take: they exercise reward types
/// every supported PoracleNG has, so the gate is not their subject and a stub that says yes keeps them
/// asserting what they were written to assert. Tests that <em>are</em> about the gate build
/// <see cref="Supporting"/> explicitly, so the reader can see which side of the fence they are on.
/// </remarks>
internal sealed class PoracleCapabilityStub(IReadOnlySet<string> supported) : IPoracleCapabilityService
{
    /// <summary>Every capability is available. For tests that do not care.</summary>
    public static IPoracleCapabilityService Permissive { get; } =
        new PoracleCapabilityStub(PoracleCapabilityKeys.All.Select(r => r.Key).ToHashSet(StringComparer.Ordinal));

    /// <summary>Nothing is available, as against a PoracleNG that is unreachable or on the released line.</summary>
    public static IPoracleCapabilityService None { get; } =
        new PoracleCapabilityStub(new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Exactly the named capabilities are available.</summary>
    public static IPoracleCapabilityService Supporting(params string[] capabilities) =>
        new PoracleCapabilityStub(capabilities.ToHashSet(StringComparer.Ordinal));

    public Task<IReadOnlySet<string>> GetSupportedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(supported);

    public Task<bool> SupportsAsync(string capability, CancellationToken cancellationToken = default) =>
        Task.FromResult(supported.Contains(capability));

    public Task EnsureSupportedAsync(string capability, CancellationToken cancellationToken = default)
    {
        if (supported.Contains(capability))
        {
            return Task.CompletedTask;
        }

        var rule = PoracleCapabilityKeys.All.FirstOrDefault(r => r.Key == capability);

        throw new PoracleUnsupportedException(capability, rule?.Requires ?? "a newer PoracleNG");
    }
}
