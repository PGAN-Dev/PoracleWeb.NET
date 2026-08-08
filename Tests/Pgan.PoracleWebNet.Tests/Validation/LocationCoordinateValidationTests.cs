using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Pgan.PoracleWebNet.Api.Controllers;

namespace Pgan.PoracleWebNet.Tests.Validation;

/// <summary>
/// As non-nullable doubles, an omitted latitude or longitude bound to 0.0 and passed [Range], so a request
/// that mentioned neither wrote 0,0 over the user's real location — the outcome the range checks were added
/// to prevent, reached by the one path they could not see. See #480.
/// </summary>
public class LocationCoordinateValidationTests
{
    private static List<ValidationResult> Validate(string json)
    {
        var request = JsonSerializer.Deserialize<LocationController.LocationUpdateRequest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void AnEmptyBodyIsRejected()
    {
        Assert.NotEmpty(Validate("{}"));
    }

    [Fact]
    public void AnOmittedLongitudeIsRejected()
    {
        var errors = Validate(/*lang=json,strict*/ "{\"latitude\":37.684826}");

        Assert.Contains(errors, e => e.MemberNames.Contains("Longitude"));
    }

    [Fact]
    public void AnOmittedLatitudeIsRejected()
    {
        var errors = Validate(/*lang=json,strict*/ "{\"longitude\":-77.6133}");

        Assert.Contains(errors, e => e.MemberNames.Contains("Latitude"));
    }

    [Fact]
    public void ZeroZeroIsStillAcceptedWhenActuallyAskedFor()
    {
        // Null Island is a real coordinate. The defect was inferring it, not permitting it.
        Assert.Empty(Validate(/*lang=json,strict*/ "{\"latitude\":0,\"longitude\":0}"));
    }

    [Fact]
    public void BothCoordinatesPresentAndInRangeIsAccepted()
    {
        Assert.Empty(Validate(/*lang=json,strict*/ "{\"latitude\":37.684826,\"longitude\":-77.6133}"));
    }

    [Fact]
    public void OutOfRangeIsStillRejected()
    {
        Assert.NotEmpty(Validate(/*lang=json,strict*/ "{\"latitude\":91,\"longitude\":0}"));
    }
}
