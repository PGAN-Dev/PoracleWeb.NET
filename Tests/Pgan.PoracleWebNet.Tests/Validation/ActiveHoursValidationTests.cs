using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Validation;

public class ActiveHoursValidationTests
{
    [Fact]
    public void ValidSingleEntry()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidNull()
    {
        var (isValid, error) = ActiveHoursValidator.Validate(null);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidEmptyString()
    {
        var (isValid, error) = ActiveHoursValidator.Validate("");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidEmptyArray()
    {
        var (isValid, error) = ActiveHoursValidator.Validate("[]");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidMultipleEntries()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"},{\"day\":2,\"hours\":\"18\",\"mins\":\"30\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidBoundaryDay1()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"00\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidBoundaryDay7()
    {
        var json = /*lang=json,strict*/ "[{\"day\":7,\"hours\":\"23\",\"mins\":\"59\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidDay0()
    {
        var json = /*lang=json,strict*/ "[{\"day\":0,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidDay8()
    {
        var json = /*lang=json,strict*/ "[{\"day\":8,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidHours25()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"25\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMins60()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"60\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidTooManyEntries()
    {
        var entries = string.Join(",", Enumerable.Range(0, 29).Select(i =>
            $"{{\"day\":{(i % 7) + 1},\"hours\":\"{i % 24:D2}\",\"mins\":\"00\"}}"));
        var json = $"[{entries}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("28", error!);
    }

    [Fact]
    public void InvalidMalformedJson()
    {
        var (isValid, error) = ActiveHoursValidator.Validate("{not json");
        Assert.False(isValid);
        Assert.Contains("JSON", error!);
    }

    [Fact]
    public void InvalidNotAnArray()
    {
        var (isValid, error) = ActiveHoursValidator.Validate(/*lang=json,strict*/ "{\"day\":1}");
        Assert.False(isValid);
        Assert.Contains("array", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMissingHoursField()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMissingMinsField()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid28Entries()
    {
        var entries = string.Join(",", Enumerable.Range(0, 28).Select(i =>
            $"{{\"day\":{(i % 7) + 1},\"hours\":\"{i % 24:D2}\",\"mins\":\"00\"}}"));
        var json = $"[{entries}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidWithWhitespace()
    {
        var json = /*lang=json,strict*/ "  [{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]  ";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidWhitespaceOnly()
    {
        var (isValid, error) = ActiveHoursValidator.Validate("   ");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidDayAsString()
    {
        var json = /*lang=json,strict*/ "[{\"day\":\"3\",\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidNegativeHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"-1\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidNegativeMins()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"-5\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidNegativeDay()
    {
        var json = /*lang=json,strict*/ "[{\"day\":-1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidFloatHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"9.5\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidBooleanHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":true,\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidExtremelyLargeHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"999999\",\"mins\":\"00\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    // --- Repeating ranges (#808). PoracleNG has always carried step/end_hours/end_mins; the
    // legitimate-case tests below are the ones that catch a tightening mistake, since every profile
    // save re-sends the whole schedule and a wrongly-refused row would 400 an unrelated rename.

    [Fact]
    public void ValidNumericRange()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":17,\"end_mins\":0,\"step\":2}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidZeroPaddedStringRange()
    {
        // Verified against a live 5.2.1 instance: this form persists verbatim through /api/profiles/{id}/update.
        var json = /*lang=json,strict*/ "[{\"day\":2,\"hours\":\"09\",\"mins\":\"00\",\"end_hours\":\"17\",\"end_mins\":\"30\",\"step\":\"3\"}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidRangeWithOmittedEndMins()
    {
        // end_mins carries omitempty upstream, so a range ending on the hour arrives without it.
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":17,\"step\":1}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidStepZeroIsSingleFire()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"step\":0}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidEndFieldsWithoutStepAreIgnored()
    {
        // No step means no range, and upstream's Fires() ignores the end fields. Refusing them would
        // break a row nothing else objects to.
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":3,\"end_mins\":0}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidStepAboveTwentyThree()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":17,\"end_mins\":0,\"step\":24}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("step", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidRangeEndEqualsStart()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":9,\"end_mins\":0,\"step\":1}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("end after it starts", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidRangeEndBeforeStart()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":22,\"mins\":0,\"end_hours\":2,\"end_mins\":0,\"step\":1}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("end after it starts", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidRangeMissingEndHours()
    {
        // step > 0 with no end at all leaves the end at 00:00, which is never after a later start.
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"step\":2}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("end after it starts", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidRangeEndHoursOutOfRange()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":9,\"mins\":0,\"end_hours\":25,\"end_mins\":0,\"step\":1}]";
        var (isValid, error) = ActiveHoursValidator.Validate(json);
        Assert.False(isValid);
        Assert.Contains("end_hours", error!, StringComparison.OrdinalIgnoreCase);
    }
}
