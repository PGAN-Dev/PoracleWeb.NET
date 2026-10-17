using System.ComponentModel.DataAnnotations;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Validation;

/// <summary>
/// <c>personal</c> refuses only the impossible (a negative mask). Bits 4 and 8 are reserved by
/// PoracleNG and a rule holding them has to stay editable, so the upper bound is not tightened
/// to the bits PoracleWeb.NET understands.
/// </summary>
public class PersonalRangeValidationTests
{
    private static bool ValidatePersonal(object instance, object? value)
    {
        var context = new ValidationContext(instance) { MemberName = "Personal" };
        return Validator.TryValidateProperty(value, context, []);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(15)]
    public void MonsterCreatePersonalAcceptsReservedBits(int value) => Assert.True(ValidatePersonal(new MonsterCreate(), value));

    [Fact]
    public void MonsterCreatePersonalRejectsNegative() => Assert.False(ValidatePersonal(new MonsterCreate(), -1));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public void MonsterUpdatePersonalAcceptsReservedBits(int value) => Assert.True(ValidatePersonal(new MonsterUpdate(), value));

    [Fact]
    public void MonsterUpdatePersonalRejectsNegative() => Assert.False(ValidatePersonal(new MonsterUpdate(), -1));

    [Fact]
    public void MonsterUpdatePersonalAcceptsNull() => Assert.True(ValidatePersonal(new MonsterUpdate(), null));
}
