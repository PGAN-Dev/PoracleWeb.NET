using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// A new alarm service that rotates uids but never remaps them reintroduces #403 silently: quick-pick
/// removal reports success, deletes nothing, and the summary read then wipes the applied state so the
/// user cannot retry. Nothing about that failure is visible without knowing to look, so it is pinned here.
/// </summary>
public class TrackedUidRemapperCoverageTests
{
    /// <summary>
    /// Every service whose update rotates the uid.
    /// </summary>
    /// <remarks>
    /// Monsters were the exception until #805: PoracleNG upserts pokemon in place on the v1 surface, so
    /// there was no rotation to follow, and this class carried a fact asserting MonsterService did NOT take
    /// the remapper. The v2 full-replace PUT is delete-then-insert, so pokemon rotates like the other nine
    /// whenever the server carries v2. The tripwire fired exactly as it was written to; it has been moved
    /// rather than weakened.
    /// </remarks>
    public static TheoryData<Type> RotatingServices =>
    [
        typeof(MonsterService), typeof(RaidService), typeof(EggService), typeof(QuestService),
        typeof(NestService), typeof(GymService), typeof(FortChangeService), typeof(InvasionService),
        typeof(LureService), typeof(MaxBattleService)
    ];

    [Theory]
    [MemberData(nameof(RotatingServices))]
    public void RotatingServicesTakeTheUidRemapper(Type serviceType)
    {
        var takesRemapper = serviceType
            .GetConstructors()
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITrackedUidRemapper)));

        Assert.True(
            takesRemapper,
            $"{serviceType.Name} rewrites the row on update, so its uid changes. Without ITrackedUidRemapper "
            + "any quick pick that created the alarm keeps pointing at the dead uid and can never remove it.");
    }
}
