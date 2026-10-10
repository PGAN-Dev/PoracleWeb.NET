using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// Profile names accepted control and bidi-override characters and stored them verbatim. NUL, TAB and
/// line feeds broke the name wherever it was rendered, and U+202E made "evil" display as "live" -- a name
/// that reads as something other than what it is. Production held none of either when this was written
/// (0 of 457 names), so refusing them turns away nothing anyone relies on.
/// </summary>
public class ProfileNameRulesTests
{
    [Theory]
    [InlineData("nul\u0000byte")]
    [InlineData("tab\tbed")]
    [InlineData("line\nfeed")]
    [InlineData("carriage\rreturn")]
    [InlineData("start\u0001of heading")]
    [InlineData("delete\u007F")]
    [InlineData("next\u0085line")]
    [InlineData("evil\u202Elive")]
    [InlineData("embed\u202Aleft")]
    [InlineData("isolate\u2066left")]
    [InlineData("first strong\u2068isolate")]
    [InlineData("mark\u200Fright")]
    [InlineData("arabic\u061Cmark")]
    [InlineData("line\u2028separator")]
    [InlineData("paragraph\u2029separator")]
    public void ANameCarryingAControlOrDirectionCharacterIsRefused(string name) =>
        Assert.NotNull(ProfileNameRules.Validate(name));

    /// <summary>
    /// What must keep working. Emoji sequences are built with U+200D (zero-width joiner), a format
    /// character like the bidi controls, so a rule that refused every format character would have refused
    /// a family emoji. Accents, non-Latin scripts and the " (2)" suffix import adds all stay legal.
    /// </summary>
    [Theory]
    [InlineData("Home")]
    [InlineData("Pokémon Go")]
    [InlineData("Ärger im Büro")]
    [InlineData("日本語のプロファイル")]
    [InlineData("עבודה")]
    [InlineData("🐉 Dragons")]
    [InlineData("👨\u200D👩\u200D👧 family")]
    [InlineData("🏳️\u200D🌈 pride")]
    [InlineData("Work (2)")]
    [InlineData("Evening 8:00-11:00")]
    public void AnOrdinaryNameIsAccepted(string name) => Assert.Null(ProfileNameRules.Validate(name));

    [Fact]
    public void ANameAtTheColumnLimitIsAccepted() =>
        Assert.Null(ProfileNameRules.Validate(new string('a', ProfileNameRules.MaxLength)));

    [Fact]
    public void ABlankNameIsStillRefused() => Assert.NotNull(ProfileNameRules.Validate("   "));
}
