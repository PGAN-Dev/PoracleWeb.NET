using System.Net;
using System.Net.Http;
using System.Text;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG 5.2.1 replaced <c>{status, message}</c> error bodies with RFC 9457 problem+json on its v2
/// surface, while v1 kept the old shape. Both have to keep working: a 5.1.0 server is still supported,
/// so a reader that understood only the new shape would take the explanation away from exactly the
/// installs that have it today.
///
/// These cases came from the v1 side of the proxy and now exercise the same reader the v2 path uses.
/// </summary>
public class PoracleProblemDetailsDescribeTests
{
    private const string Fallback = PoracleProblemDetails.Unexplained;

    private static string Describe(string body, string contentType = "application/json")
    {
        _ = contentType; // Describe reads the body; the header never decides the shape.
        return PoracleProblemDetails.Describe(body);
    }

    // ──────────────────────────────────────────────────────────────
    // problem+json (PoracleNG 5.2.1)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ReadsDetailFromProblemJson()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed"}""");

        Assert.Equal("validation failed", message);
    }

    /// <summary>
    /// errors[] names the fields that were refused, which is more use than the summary in detail, so it
    /// wins when both are present.
    /// </summary>
    [Fact]
    public void PrefersFieldErrorsOverDetail()
    {
        var message = Describe(
            /*lang=json,strict*/ """
            {"title":"Unprocessable Entity","status":422,"detail":"validation failed",
             "errors":[{"message":"expected number <= 100","location":"body.min_iv","value":200}]}
            """);

        Assert.Equal("min_iv: expected number <= 100", message);
    }

    /// <summary>
    /// location arrives as a path into the submitted body. Only the last segment means anything to
    /// someone looking at the form that produced it.
    /// </summary>
    [Fact]
    public void TrimsTheBodyPrefixFromAFieldLocation()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"errors":[{"message":"required","location":"body.pokemon_id"}]}""");

        Assert.StartsWith("pokemon_id:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void SummarisesWhenMoreThanThreeFieldsWereRefused()
    {
        var message = Describe(
            /*lang=json,strict*/ """
            {"errors":[{"message":"a","location":"body.one"},{"message":"b","location":"body.two"},
                       {"message":"c","location":"body.three"},{"message":"d","location":"body.four"},
                       {"message":"e","location":"body.five"}]}
            """);

        Assert.Contains("one: a", message, StringComparison.Ordinal);
        Assert.Contains("(and 2 more)", message, StringComparison.Ordinal);
        Assert.DoesNotContain("five", message, StringComparison.Ordinal);
    }

    /// <summary>title is the status phrase, so it answers only when nothing better is on the wire.</summary>
    [Fact]
    public void FallsBackToTitleWhenThereIsNoDetail()
    {
        var message = Describe(/*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422}""");

        Assert.Equal("Unprocessable Entity", message);
    }

    /// <summary>
    /// The old shape put a word in status; problem+json puts the HTTP code there. Answering a user with
    /// "422" explains nothing, so a numeric status is never the message.
    /// </summary>
    [Fact]
    public void NeverReturnsANumericStatusAsTheMessage()
    {
        var message = Describe(/*lang=json,strict*/ """{"status":422}""");

        Assert.Equal(Fallback, message);
    }

    /// <summary>A server that sends problem+json without setting the header is still understood.</summary>
    [Fact]
    public void DoesNotDependOnTheContentTypeHeader()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"detail":"validation failed"}""",
            "application/problem+json");

        Assert.Equal("validation failed", message);
    }

    // ──────────────────────────────────────────────────────────────
    // Captured verbatim from a live PoracleNG 5.2.1 (dev-01 :3042)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// v2 rejects unknown properties. Note the location carries an array index -- body[0].x -- because
    /// tracking bodies are arrays of rules; only the trailing segment is shown to the user.
    /// </summary>
    [Fact]
    public void ReadsALiveUnknownPropertyRejection()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"unexpected property","location":"body[0].bogus_field","value":{"bogus_field":1,"pokemon_id":25}}]}""",
            "application/problem+json");

        Assert.Equal("bogus_field: unexpected property", message);
    }

    [Fact]
    public void ReadsALiveTypeMismatchRejection()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"expected integer","location":"body[0].pokemon_id","value":"twenty-five"}]}""",
            "application/problem+json");

        Assert.Equal("pokemon_id: expected integer", message);
    }

    /// <summary>A semantic refusal carries no errors[], so detail is the whole explanation.</summary>
    [Fact]
    public void ReadsALiveSemanticRejectionWithNoFieldErrors()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"unknown display_type"}""",
            "application/problem+json");

        Assert.Equal("unknown display_type", message);
    }

    /// <summary>
    /// 5.2.1's frozen v1 surface still answers in the old shape -- captured from the same server that
    /// produced the problem+json above. This is why the reader must keep both.
    /// </summary>
    [Fact]
    public void ReadsALive521V1Rejection()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"message":"Grunt type mandatory","status":"error"}""");

        Assert.Equal("Grunt type mandatory", message);
    }

    // ──────────────────────────────────────────────────────────────
    // {status, message} (PoracleNG 5.1.0) — must keep working
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void StillReadsTheLegacyMessageProperty()
    {
        var message = Describe(
            /*lang=json,strict*/ """{"status":"error","message":"Grunt type mandatory"}""");

        Assert.Equal("Grunt type mandatory", message);
    }

    [Fact]
    public void StillReadsTheLegacyErrorProperty()
    {
        var message = Describe(/*lang=json,strict*/ """{"error":"An unexpected error occurred."}""");

        Assert.Equal("An unexpected error occurred.", message);
    }

    // ──────────────────────────────────────────────────────────────
    // Neither shape
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void EchoesAShortNonJsonBody()
    {
        var message = Describe("upstream connect error", "text/plain");

        Assert.Equal("upstream connect error", message);
    }

    [Fact]
    public void FallsBackWhenTheBodyIsTooLongToShow()
    {
        var message = Describe(new string('x', 301), "text/plain");

        Assert.Equal(Fallback, message);
    }

    [Fact]
    public void FallsBackWhenThereIsNoBody()
    {
        var message = Describe(string.Empty, "text/plain");

        Assert.Equal(Fallback, message);
    }
}
