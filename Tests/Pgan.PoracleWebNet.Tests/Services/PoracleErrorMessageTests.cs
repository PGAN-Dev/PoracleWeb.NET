using System.Net;
using System.Net.Http;
using System.Text;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleNG 5.2.1 replaced <c>{status, message}</c> error bodies with RFC 9457 problem+json. Both
/// shapes have to keep working: a 5.1.0 server is still supported, so a reader that understood only
/// the new shape would take the explanation away from exactly the installs that have it today.
/// </summary>
public class PoracleErrorMessageTests
{
    private const string Fallback = "Poracle rejected the alarm.";

    private static Task<string> ExtractAsync(string body, string contentType = "application/json")
    {
        var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };

        return PoracleErrorMessage.ExtractAsync(response, Fallback);
    }

    // ──────────────────────────────────────────────────────────────
    // problem+json (PoracleNG 5.2.1)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadsDetailFromProblemJson()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed"}""");

        Assert.Equal("validation failed", message);
    }

    /// <summary>
    /// errors[] names the fields that were refused, which is more use than the summary in detail, so it
    /// wins when both are present.
    /// </summary>
    [Fact]
    public async Task PrefersFieldErrorsOverDetail()
    {
        var message = await ExtractAsync(
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
    public async Task TrimsTheBodyPrefixFromAFieldLocation()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"errors":[{"message":"required","location":"body.pokemon_id"}]}""");

        Assert.StartsWith("pokemon_id:", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarisesWhenMoreThanThreeFieldsWereRefused()
    {
        var message = await ExtractAsync(
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
    public async Task FallsBackToTitleWhenThereIsNoDetail()
    {
        var message = await ExtractAsync(/*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422}""");

        Assert.Equal("Unprocessable Entity", message);
    }

    /// <summary>
    /// The old shape put a word in status; problem+json puts the HTTP code there. Answering a user with
    /// "422" explains nothing, so a numeric status is never the message.
    /// </summary>
    [Fact]
    public async Task NeverReturnsANumericStatusAsTheMessage()
    {
        var message = await ExtractAsync(/*lang=json,strict*/ """{"status":422}""");

        Assert.Equal(Fallback, message);
    }

    /// <summary>A server that sends problem+json without setting the header is still understood.</summary>
    [Fact]
    public async Task DoesNotDependOnTheContentTypeHeader()
    {
        var message = await ExtractAsync(
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
    public async Task ReadsALiveUnknownPropertyRejection()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"unexpected property","location":"body[0].bogus_field","value":{"bogus_field":1,"pokemon_id":25}}]}""",
            "application/problem+json");

        Assert.Equal("bogus_field: unexpected property", message);
    }

    [Fact]
    public async Task ReadsALiveTypeMismatchRejection()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"validation failed","errors":[{"message":"expected integer","location":"body[0].pokemon_id","value":"twenty-five"}]}""",
            "application/problem+json");

        Assert.Equal("pokemon_id: expected integer", message);
    }

    /// <summary>A semantic refusal carries no errors[], so detail is the whole explanation.</summary>
    [Fact]
    public async Task ReadsALiveSemanticRejectionWithNoFieldErrors()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"title":"Unprocessable Entity","status":422,"detail":"unknown display_type"}""",
            "application/problem+json");

        Assert.Equal("unknown display_type", message);
    }

    /// <summary>
    /// 5.2.1's frozen v1 surface still answers in the old shape -- captured from the same server that
    /// produced the problem+json above. This is why the reader must keep both.
    /// </summary>
    [Fact]
    public async Task ReadsALive521V1Rejection()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"message":"Grunt type mandatory","status":"error"}""");

        Assert.Equal("Grunt type mandatory", message);
    }

    // ──────────────────────────────────────────────────────────────
    // {status, message} (PoracleNG 5.1.0) — must keep working
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StillReadsTheLegacyMessageProperty()
    {
        var message = await ExtractAsync(
            /*lang=json,strict*/ """{"status":"error","message":"Grunt type mandatory"}""");

        Assert.Equal("Grunt type mandatory", message);
    }

    [Fact]
    public async Task StillReadsTheLegacyErrorProperty()
    {
        var message = await ExtractAsync(/*lang=json,strict*/ """{"error":"An unexpected error occurred."}""");

        Assert.Equal("An unexpected error occurred.", message);
    }

    /// <summary>A string status was the last resort in the old shape and still is.</summary>
    [Fact]
    public async Task StillReadsAStringStatusWhenItIsAllThereIs()
    {
        var message = await ExtractAsync(/*lang=json,strict*/ """{"status":"Grunt type mandatory"}""");

        Assert.Equal("Grunt type mandatory", message);
    }

    // ──────────────────────────────────────────────────────────────
    // Neither shape
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EchoesAShortNonJsonBody()
    {
        var message = await ExtractAsync("upstream connect error", "text/plain");

        Assert.Equal("upstream connect error", message);
    }

    [Fact]
    public async Task FallsBackWhenTheBodyIsTooLongToShow()
    {
        var message = await ExtractAsync(new string('x', 301), "text/plain");

        Assert.Equal(Fallback, message);
    }

    [Fact]
    public async Task FallsBackWhenThereIsNoBody()
    {
        var message = await ExtractAsync(string.Empty, "text/plain");

        Assert.Equal(Fallback, message);
    }
}
