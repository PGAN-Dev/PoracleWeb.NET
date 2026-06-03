using System.ComponentModel.DataAnnotations;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Validation;

/// <summary>
/// Guards the widened <c>clean</c> range on the 8 alarm Create/Update types that previously
/// capped it at <c>[Range(0,1)]</c>. PoracleNG reads <c>clean</c> as a 3-bit bitmask
/// (bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary), so a bot-set value up to 7
/// must round-trip through a web edit instead of 400ing. Raid/Egg are covered separately by
/// <see cref="RsvpRangeValidationTests"/>. (#292)
/// </summary>
public class CleanRangeValidationTests
{
    private static bool ValidateClean(object instance, object? value)
    {
        var context = new ValidationContext(instance) { MemberName = "Clean" };
        var results = new List<ValidationResult>();
        return Validator.TryValidateProperty(value, context, results);
    }

    public static TheoryData<int> AcceptedValues =>
        new() { 0, 1, 2, 3, 4, 5, 6, 7 };

    public static TheoryData<int> RejectedValues =>
        new() { -1, 8 };

    // ── Create types ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void MonsterCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new MonsterCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void MonsterCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new MonsterCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void QuestCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new QuestCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void QuestCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new QuestCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void InvasionCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new InvasionCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void InvasionCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new InvasionCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void LureCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new LureCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void LureCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new LureCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void NestCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new NestCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void NestCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new NestCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void GymCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new GymCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void GymCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new GymCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void MaxBattleCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new MaxBattleCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void MaxBattleCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new MaxBattleCreate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void FortChangeCreateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new FortChangeCreate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void FortChangeCreateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new FortChangeCreate(), value));

    // ── Update types ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void MonsterUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new MonsterUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void MonsterUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new MonsterUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void QuestUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new QuestUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void QuestUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new QuestUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void InvasionUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new InvasionUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void InvasionUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new InvasionUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void LureUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new LureUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void LureUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new LureUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void NestUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new NestUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void NestUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new NestUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void GymUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new GymUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void GymUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new GymUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void MaxBattleUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new MaxBattleUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void MaxBattleUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new MaxBattleUpdate(), value));

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void FortChangeUpdateCleanAcceptsBitmaskRange(int value) => Assert.True(ValidateClean(new FortChangeUpdate(), value));

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void FortChangeUpdateCleanRejectsOutOfRange(int value) => Assert.False(ValidateClean(new FortChangeUpdate(), value));

    // Update types allow null (null-skip merge semantics) — null must always validate.

    [Fact]
    public void MonsterUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new MonsterUpdate(), null));

    [Fact]
    public void QuestUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new QuestUpdate(), null));

    [Fact]
    public void InvasionUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new InvasionUpdate(), null));

    [Fact]
    public void LureUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new LureUpdate(), null));

    [Fact]
    public void NestUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new NestUpdate(), null));

    [Fact]
    public void GymUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new GymUpdate(), null));

    [Fact]
    public void MaxBattleUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new MaxBattleUpdate(), null));

    [Fact]
    public void FortChangeUpdateCleanAcceptsNull() => Assert.True(ValidateClean(new FortChangeUpdate(), null));
}
