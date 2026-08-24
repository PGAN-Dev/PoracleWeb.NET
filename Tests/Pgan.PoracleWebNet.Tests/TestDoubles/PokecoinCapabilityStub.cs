using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.TestDoubles;

/// <summary>
/// A pokecoin capability that answers whatever the test needs, without a live PoracleNG behind it.
/// </summary>
/// <remarks>
/// <see cref="Supported"/> is the default for every test that is not about pokecoins at all: it keeps
/// the reward types that work on every server working, which is exactly what a gate must not break.
/// </remarks>
public sealed class PokecoinCapabilityStub(bool supported) : IQuestPokecoinCapabilityService
{
    /// <summary>A PoracleNG 5.2.0 or newer.</summary>
    public static PokecoinCapabilityStub Supported => new(true);

    /// <summary>A PoracleNG 5.1.0, or one that could not be reached.</summary>
    public static PokecoinCapabilityStub Unsupported => new(false);

    public Task<bool> ArePokecoinRewardsSupportedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(supported);
}
