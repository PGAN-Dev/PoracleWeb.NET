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
    /// Every service whose update rotates the uid — all of them except monsters, which PoracleNG
    /// genuinely upserts in place.
    /// </summary>
    public static TheoryData<Type> RotatingServices =>
    [
        typeof(RaidService), typeof(EggService), typeof(QuestService), typeof(NestService),
        typeof(GymService), typeof(FortChangeService), typeof(InvasionService), typeof(LureService),
        typeof(MaxBattleService)
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

    [Fact]
    public void MonsterServiceDoesNotNeedIt()
    {
        // Documented deliberately: monsters are the one type PoracleNG updates in place, so there is no
        // rotation to follow. If that ever changes, this test failing is the reminder.
        var takesRemapper = typeof(MonsterService)
            .GetConstructors()
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITrackedUidRemapper)));

        Assert.False(takesRemapper);
    }
}
