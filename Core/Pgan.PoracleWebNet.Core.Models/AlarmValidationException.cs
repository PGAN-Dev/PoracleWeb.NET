namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A service-layer rejection of an alarm the caller cannot fix by retrying: the request itself is wrong.
/// </summary>
/// <remarks>
/// Service-layer guards used to throw <see cref="ArgumentException"/>, which nothing maps, so a request the
/// guard was written to explain came back as a bare 500 and the explanation never left the building. The
/// create paths get the same rejection as a 400 from model validation, so the update paths were reporting a
/// server fault for a request the API already knew how to describe. See #518.
/// </remarks>
public sealed class AlarmValidationException : Exception
{
    public AlarmValidationException(string message)
        : base(message)
    {
    }

    public AlarmValidationException()
    {
    }

    public AlarmValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
