using System.ComponentModel.DataAnnotations;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Validation;

public class RsvpRangeValidationTests
{
    private static bool ValidateProperty(object instance, string propertyName, object? value)
    {
        var context = new ValidationContext(instance) { MemberName = propertyName };
        var results = new List<ValidationResult>();
        return Validator.TryValidateProperty(value, context, results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RaidCreateRsvpChangesAcceptsValidRange(int value) => Assert.True(ValidateProperty(new RaidCreate(), nameof(RaidCreate.RsvpChanges), value));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RaidCreateRsvpChangesRejectsOutOfRange(int value) => Assert.False(ValidateProperty(new RaidCreate(), nameof(RaidCreate.RsvpChanges), value));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EggCreateRsvpChangesAcceptsValidRange(int value) => Assert.True(ValidateProperty(new EggCreate(), nameof(EggCreate.RsvpChanges), value));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EggCreateRsvpChangesRejectsOutOfRange(int value) => Assert.False(ValidateProperty(new EggCreate(), nameof(EggCreate.RsvpChanges), value));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RaidUpdateRsvpChangesAcceptsValidRange(int? value) => Assert.True(ValidateProperty(new RaidUpdate(), nameof(RaidUpdate.RsvpChanges), value));

    [Fact]
    public void RaidUpdateRsvpChangesAcceptsNull() => Assert.True(ValidateProperty(new RaidUpdate(), nameof(RaidUpdate.RsvpChanges), null));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RaidUpdateRsvpChangesRejectsOutOfRange(int value) => Assert.False(ValidateProperty(new RaidUpdate(), nameof(RaidUpdate.RsvpChanges), value));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EggUpdateRsvpChangesAcceptsValidRange(int? value) => Assert.True(ValidateProperty(new EggUpdate(), nameof(EggUpdate.RsvpChanges), value));

    [Fact]
    public void EggUpdateRsvpChangesAcceptsNull() => Assert.True(ValidateProperty(new EggUpdate(), nameof(EggUpdate.RsvpChanges), null));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EggUpdateRsvpChangesRejectsOutOfRange(int value) => Assert.False(ValidateProperty(new EggUpdate(), nameof(EggUpdate.RsvpChanges), value));
}
