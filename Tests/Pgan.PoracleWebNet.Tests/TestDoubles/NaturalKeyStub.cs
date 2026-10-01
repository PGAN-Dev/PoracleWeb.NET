using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.TestDoubles;

/// <summary>
/// A natural-key capability that answers whatever the test needs, without a live database behind it.
/// </summary>
/// <remarks>
/// <see cref="Enforced"/> is the PoracleNG 5.1.0 world (schema 5, unique keys present) and the behaviour
/// every existing lure and invasion test was written against. <see cref="Dropped"/> is 5.2.1 and later.
/// </remarks>
public sealed class NaturalKeyStub(bool enforced) : INaturalKeyCapabilityService
{
    /// <summary>A PoracleNG whose <c>lures</c> and <c>invasion</c> tables still carry their unique keys.</summary>
    public static NaturalKeyStub Enforced => new(true);

    /// <summary>A PoracleNG past migration 8, where the keys are gone.</summary>
    public static NaturalKeyStub Dropped => new(false);

    public Task<bool> IsEnforcedAsync(string trackingType, CancellationToken cancellationToken = default) =>
        Task.FromResult(enforced);
}
