namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// PoracleNG answered a write with a success status but no explicit outcome, so nobody can say whether it
/// changed anything.
/// </summary>
/// <remarks>
/// A type of its own so a caller can treat this answer (skip, report, retry later) without also swallowing
/// the <see cref="InvalidOperationException"/> a genuine bug would throw.
/// </remarks>
public sealed class PoracleAmbiguousAnswerException : Exception
{
    public PoracleAmbiguousAnswerException()
        : base("PoracleNG answered without saying what it did.")
    {
    }

    public PoracleAmbiguousAnswerException(string message)
        : base(message)
    {
    }

    public PoracleAmbiguousAnswerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
