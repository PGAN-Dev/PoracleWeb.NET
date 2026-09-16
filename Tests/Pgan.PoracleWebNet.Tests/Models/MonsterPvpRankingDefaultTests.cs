using System.ComponentModel.DataAnnotations;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// The PVP rank floor a new alarm is created with.
/// </summary>
/// <remarks>
/// <para>
/// 0 is not a rank. PoracleNG's column defaults to 1, its bot sets 1 explicitly, and both API surfaces
/// default to 1 when the field is absent -- so every stored 0 came from here, through v1's
/// <c>flexInt</c> passing an explicitly sent value straight through. One instance holds 15,383 rules at
/// 0 against 7,770 at 1. See jfberry/PoracleNG#227.
/// </para>
/// <para>
/// The pairing with <see cref="MonsterCreate.PvpRankingWorst"/> is the tell: its sibling was given the
/// column's default and this one was left at the language default.
/// </para>
/// </remarks>
public class MonsterPvpRankingDefaultTests
{
    private static IList<ValidationResult> Validate(MonsterCreate monster)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(monster, new ValidationContext(monster), results, validateAllProperties: true);

        return results;
    }

    [Fact]
    public void ANewAlarmStartsAtTheRankFloorRatherThanAtZero()
    {
        var monster = new MonsterCreate();

        Assert.Equal(1, monster.PvpRankingBest);
    }

    [Fact]
    public void TheRankWindowDefaultsToTheWholeRange()
    {
        // Both ends, together: the bug was that one of the pair was defaulted and the other was not, so
        // asserting the floor alone would not have caught it and would not catch it coming back.
        var monster = new MonsterCreate();

        Assert.Equal(1, monster.PvpRankingBest);
        Assert.Equal(4096, monster.PvpRankingWorst);

        // Through the mapping too, since that is what actually reaches the wire.
        Assert.Null(MonsterRangeValidator.Validate(monster.ToMonster()));
    }

    [Fact]
    public void AStoredZeroIsStillAccepted()
    {
        // The legitimate-case half, and the reason the range was not tightened to (1, 4096) alongside
        // the default. Those 15,383 rules exist. Refusing the value would fail an edit on a rule the
        // user did not break -- the #835 shape -- and they are only repaired by being editable.
        var monster = new MonsterCreate { PokemonId = 25, PvpRankingBest = 0 };

        Assert.Empty(Validate(monster));
        Assert.Null(MonsterRangeValidator.Validate(monster.ToMonster()));
    }

    [Fact]
    public void ARankAboveTheCeilingIsStillRefused()
    {
        // The bound still does its job; only the default moved.
        var monster = new MonsterCreate { PokemonId = 25, PvpRankingBest = 4097 };

        Assert.Contains(Validate(monster), r => r.MemberNames.Contains(nameof(MonsterCreate.PvpRankingBest)));
    }
}
