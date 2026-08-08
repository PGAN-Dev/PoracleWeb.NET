using System.Net;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// A call to the Koji geofence server failed.
/// </summary>
/// <remarks>
/// <c>EnsureSuccessStatusCode()</c> threw a bare <see cref="HttpRequestException"/>, which no controller
/// caught, so any Koji-side failure during a geofence approval — Koji down, a region deleted between the
/// cached region list loading and the admin clicking approve, an unknown parent id — surfaced as
/// <c>500 {"error":"An unexpected error occurred."}</c> with nothing to act on. This carries the status
/// and body so the caller can answer 502 and say which upstream failed. See #422.
/// </remarks>
public sealed class KojiOperationException : Exception
{
    public KojiOperationException(string operation, HttpStatusCode statusCode, string? responseBody)
        : base($"Koji rejected the {operation} request with {(int)statusCode} {statusCode}.")
    {
        this.Operation = operation;
        this.StatusCode = statusCode;
        this.ResponseBody = responseBody;
    }

    public KojiOperationException()
    {
    }

    public KojiOperationException(string message)
        : base(message)
    {
    }

    public KojiOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Operation
    {
        get;
    }

    public HttpStatusCode StatusCode
    {
        get;
    }

    public string? ResponseBody
    {
        get;
    }
}
