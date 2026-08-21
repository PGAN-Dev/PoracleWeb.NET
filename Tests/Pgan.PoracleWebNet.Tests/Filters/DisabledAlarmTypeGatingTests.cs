using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// A disabled alarm type is gone, not read-only: the gate sits on the controller, so every action —
/// reads and deletes included — answers 403 until it is switched back on.
/// </summary>
/// <remarks>
/// <para>
/// This was briefly the other way round. #784 moved the attribute onto the write actions so a user
/// could still see and remove rules of a type that had been switched off. That left a page reachable
/// for a feature an operator had turned off, so it was reverted (#792): an operator disabling a type
/// means it should disappear, and dormant rules are harmless — they cannot fire, and they come back
/// intact if the type is re-enabled.
/// </para>
/// <para>
/// Asserted on the attribute rather than through the pipeline because the failure being guarded
/// against is someone moving the gate back onto individual actions, which no behavioural test of the
/// actions that exist today would notice.
/// </para>
/// </remarks>
public class DisabledAlarmTypeGatingTests
{
    public static TheoryData<Type, string> GatedControllers() => new()
    {
        { typeof(MonsterController), "disable_mons" },
        { typeof(RaidController), "disable_raids" },
        { typeof(EggController), "disable_raids" },
        { typeof(QuestController), "disable_quests" },
        { typeof(InvasionController), "disable_invasions" },
        { typeof(LureController), "disable_lures" },
        { typeof(NestController), "disable_nests" },
        { typeof(GymController), "disable_gyms" },
        { typeof(FortChangeController), "disable_fort_changes" },
        { typeof(MaxBattleController), "disable_maxbattles" },
        { typeof(SummaryScheduleController), "disable_quests" },
    };

    [Theory]
    [MemberData(nameof(GatedControllers))]
    public void ControllerIsGatedAtClassLevel(Type controller, string expectedKey)
    {
        var attrs = controller.GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), inherit: true)
            .Cast<RequireFeatureEnabledAttribute>()
            .ToList();

        Assert.True(
            attrs.Count == 1,
            $"{controller.Name} should carry exactly one class-level [RequireFeatureEnabled]. A per-action gate leaves "
            + "the page reachable and its reads answering for a type the operator switched off.");
        Assert.Equal(expectedKey, (string)attrs[0].Arguments![0]);
    }

    /// <summary>Eggs share the raid key, so disabling raids takes eggs with it.</summary>
    [Fact]
    public void EggsAreGatedOnTheRaidKey()
    {
        var attr = typeof(EggController)
            .GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), inherit: true)
            .Cast<RequireFeatureEnabledAttribute>()
            .Single();

        Assert.Equal("disable_raids", (string)attr.Arguments![0]);
    }
}
