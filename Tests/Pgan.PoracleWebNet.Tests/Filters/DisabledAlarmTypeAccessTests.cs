using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// A disabled alarm type blocks new rules and edits, and leaves the ones a user already has visible
/// and removable.
/// </summary>
/// <remarks>
/// <para>
/// The previous behaviour was a class-level gate, which 403'd the reads too: switching a type off
/// hid alarms their owner had already created, and left them no way to remove rules that could never
/// fire again. When the type is disabled in Poracle rather than here, its bot refuses the matching
/// command as well, so this page is the only place left to clean up.
/// </para>
/// <para>
/// These tests read the attributes rather than exercising the pipeline on purpose: the failure being
/// guarded against is someone reinstating the class-level attribute, or adding a write action without
/// one, and neither shows up in a behavioural test of the actions that already exist.
/// </para>
/// </remarks>
public class DisabledAlarmTypeAccessTests
{
    public static TheoryData<Type> AlarmControllers() =>
    [
        typeof(MonsterController), typeof(RaidController), typeof(EggController), typeof(QuestController),
        typeof(InvasionController), typeof(LureController), typeof(NestController), typeof(GymController),
        typeof(FortChangeController), typeof(MaxBattleController),
    ];

    [Theory]
    [MemberData(nameof(AlarmControllers))]
    public void AlarmControllerHasNoClassLevelGate(Type controller)
    {
        var classGate = controller.GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), inherit: true);

        Assert.True(
            classGate.Length == 0,
            $"{controller.Name} carries a class-level [RequireFeatureEnabled], which 403s its reads and deletes too. "
            + "A disabled type must still list and remove the rules a user already has.");
    }

    [Theory]
    [MemberData(nameof(AlarmControllers))]
    public void ReadsAndDeletesStayOpen(Type controller)
    {
        var open = Actions(controller)
            .Where(m => Verb<HttpGetAttribute>(m) || Verb<HttpDeleteAttribute>(m))
            .ToList();

        Assert.NotEmpty(open);
        foreach (var action in open)
        {
            Assert.True(
                action.GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), inherit: true).Length == 0,
                $"{controller.Name}.{action.Name} is gated. Viewing and removing existing alarms must keep working "
                + "while the type is disabled.");
        }
    }

    [Theory]
    [MemberData(nameof(AlarmControllers))]
    public void EveryCreateAndEditIsGated(Type controller)
    {
        var writes = Actions(controller)
            .Where(m => Verb<HttpPostAttribute>(m) || Verb<HttpPutAttribute>(m) || Verb<HttpPatchAttribute>(m))
            .ToList();

        Assert.NotEmpty(writes);
        foreach (var action in writes)
        {
            Assert.True(
                action.GetCustomAttributes(typeof(RequireFeatureEnabledAttribute), inherit: true).Length > 0,
                $"{controller.Name}.{action.Name} creates or changes a rule and is not gated. Every write on these "
                + "controllers needs [RequireFeatureEnabled] now that the class-level attribute is gone.");
        }
    }

    /// <summary>Eggs share the raid key, so a raid-disabling operator does not leave eggs creatable.</summary>
    [Fact]
    public void EggWritesAreGatedOnTheRaidKey()
    {
        // The key is a constructor argument on the filter attribute, not a property.
        var keys = Actions(typeof(EggController))
            .SelectMany(m => m.GetCustomAttributes<RequireFeatureEnabledAttribute>(inherit: true))
            .Select(a => (string)a.Arguments![0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["disable_raids"], keys);
    }

    private static List<MethodInfo> Actions(Type controller) =>
        [.. controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)];

    private static bool Verb<T>(MethodInfo action) where T : Attribute =>
        action.GetCustomAttributes(typeof(T), inherit: true).Length > 0;
}
