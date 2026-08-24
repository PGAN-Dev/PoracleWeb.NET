using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The costume gate, stood in for by the answer a test wants.
/// </summary>
/// <remarks>
/// Every existing alarm test asserts behaviour that has nothing to do with costume, so they all take
/// <see cref="Supported"/> — a server with the columns, which is what they were written against. Naming
/// it beats a bare <c>Mock.Of&lt;&gt;()</c>: the default double would pass the gate by accident, and a
/// gate that is never exercised looks identical to one that was deleted.
/// </remarks>
internal static class CostumeCapabilityDoubles
{
    /// <summary>A PoracleNG with both costume columns. Refuses nothing.</summary>
    public static ICostumeCapabilityService Supported() => Build(new CostumeCapability(true, true));

    /// <summary>A PoracleNG with neither column. Refuses any costume but the wildcard.</summary>
    public static ICostumeCapabilityService Unsupported() => Build(CostumeCapability.None);

    /// <summary>
    /// Wraps the real refusal rule around a fixed capability, so a test that sets "unsupported" gets the
    /// production behaviour — wildcard through, anything else refused — rather than a hand-written twin
    /// of it that can drift.
    /// </summary>
    private static ICostumeCapabilityService Build(CostumeCapability capability)
    {
        var mock = new Mock<ICostumeCapabilityService>();

        mock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(capability);
        mock.Setup(c => c.EnsureCostumeWritableAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string type, int costume, CancellationToken _) =>
            {
                if (costume != CostumeCapability.AnyCostume && !capability.Supports(type))
                {
                    throw new AlarmValidationException($"This Poracle server cannot store a {type} costume filter.");
                }

                return Task.CompletedTask;
            });

        return mock.Object;
    }
}
