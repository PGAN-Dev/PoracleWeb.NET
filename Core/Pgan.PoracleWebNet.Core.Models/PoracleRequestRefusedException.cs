namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// PoracleNG refused a human, profile, area or location request and said why.
/// </summary>
/// <remarks>
/// <para>
/// <c>PoracleHumanProxy</c> handled 404 and one 409 and let everything else reach
/// <c>EnsureSuccessStatusCode()</c>, so a 400 such as "state is required (true/false)" or "profile_no must
/// be specified" arrived at the browser as 500 "An unexpected error occurred" and was logged as a server
/// fault. This is the same defect #539 closed on the tracking proxy, on the half of the surface that fix
/// did not reach.
/// </para>
/// <para>
/// Deliberately not <see cref="AlarmValidationException"/>: that type is documented as an alarm rejection
/// and shares an alarm-worded fallback with it, so reusing it would answer "Poracle rejected the alarm."
/// to someone renaming a profile or saving a place. One narrowly-named exception per failure shape is the
/// pattern the rest of the filters already follow.
/// </para>
/// <para>
/// <see cref="StatusCode"/> carries the answer through because not every refusal is a 400: PoracleNG
/// answers 404 "Profile not found" for a profile number that does not exist, which is the caller naming
/// something absent, not the account being gone.
/// </para>
/// </remarks>
public sealed class PoracleRequestRefusedException : Exception
{
    public PoracleRequestRefusedException(string message)
        : base(message)
    {
    }

    public PoracleRequestRefusedException(string message, int statusCode)
        : base(message) => this.StatusCode = statusCode;

    public PoracleRequestRefusedException()
    {
    }

    public PoracleRequestRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The status the API should answer. 400 unless PoracleNG named something absent.</summary>
    public int StatusCode
    {
        get; init;
    } = 400;
}
