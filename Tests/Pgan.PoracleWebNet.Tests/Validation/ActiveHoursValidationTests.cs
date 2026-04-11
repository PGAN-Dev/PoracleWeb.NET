using Pgan.PoracleWebNet.Api.Controllers;

namespace Pgan.PoracleWebNet.Tests.Validation;

public class ActiveHoursValidationTests
{
    [Fact]
    public void ValidSingleEntry()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidNull()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours(null);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidEmptyString()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours("");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidEmptyArray()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours("[]");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidMultipleEntries()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"},{\"day\":2,\"hours\":\"18\",\"mins\":\"30\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidBoundaryDay1()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"00\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidBoundaryDay7()
    {
        var json = /*lang=json,strict*/ "[{\"day\":7,\"hours\":\"23\",\"mins\":\"59\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidDay0()
    {
        var json = /*lang=json,strict*/ "[{\"day\":0,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidDay8()
    {
        var json = /*lang=json,strict*/ "[{\"day\":8,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidHours25()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"25\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMins60()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"60\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidTooManyEntries()
    {
        var entries = string.Join(",", Enumerable.Range(0, 29).Select(i =>
            $"{{\"day\":{(i % 7) + 1},\"hours\":\"{i % 24:D2}\",\"mins\":\"00\"}}"));
        var json = $"[{entries}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("28", error!);
    }

    [Fact]
    public void InvalidMalformedJson()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours("{not json");
        Assert.False(isValid);
        Assert.Contains("JSON", error!);
    }

    [Fact]
    public void InvalidNotAnArray()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours(/*lang=json,strict*/ "{\"day\":1}");
        Assert.False(isValid);
        Assert.Contains("array", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMissingHoursField()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidMissingMinsField()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid28Entries()
    {
        var entries = string.Join(",", Enumerable.Range(0, 28).Select(i =>
            $"{{\"day\":{(i % 7) + 1},\"hours\":\"{i % 24:D2}\",\"mins\":\"00\"}}"));
        var json = $"[{entries}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidWithWhitespace()
    {
        var json = /*lang=json,strict*/ "  [{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]  ";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidWhitespaceOnly()
    {
        var (isValid, error) = ProfileController.ValidateActiveHours("   ");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidDayAsString()
    {
        var json = /*lang=json,strict*/ "[{\"day\":\"3\",\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidNegativeHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"-1\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidNegativeMins()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"-5\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("mins", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidNegativeDay()
    {
        var json = /*lang=json,strict*/ "[{\"day\":-1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("day", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidFloatHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"9.5\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidBooleanHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":true,\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidExtremelyLargeHours()
    {
        var json = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"999999\",\"mins\":\"00\"}]";
        var (isValid, error) = ProfileController.ValidateActiveHours(json);
        Assert.False(isValid);
        Assert.Contains("hours", error!, StringComparison.OrdinalIgnoreCase);
    }
}
