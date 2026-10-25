namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>A create asked for a destination that cannot take it. Carries the status the API answers with.</summary>
public sealed class AlarmDestinationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
