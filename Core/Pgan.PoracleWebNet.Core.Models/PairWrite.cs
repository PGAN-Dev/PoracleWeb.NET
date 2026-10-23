namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>The other side of a linked pair did not take a write the signed-in side already took.</summary>
public sealed record PairWarning(LinkSide Side, string Reason)
{
    /// <summary>The shape a response carries as <c>pairWarning</c>.</summary>
    public PairWarningBody ToBody() => new(AccountLinkKeys.Wire(this.Side), this.Reason);
}

/// <summary>Wire form of <see cref="PairWarning"/>: <c>{ side: "telegram", reason }</c>.</summary>
public sealed record PairWarningBody(string Side, string Reason);

/// <summary>
/// What a person-level write did. <see cref="Other"/> is set only when a second human took the write, and
/// <see cref="Warning"/> only when that second write failed.
/// </summary>
public sealed record PairWriteResult<T>(T Primary, T? Other, PairWarning? Warning);

/// <summary>
/// The other human of a pair was left alone on purpose (its profile has drifted, say). The message is written
/// for the person and is carried to the client as the warning's reason.
/// </summary>
public sealed class PairWriteRefusedException : Exception
{
    public PairWriteRefusedException(string reason)
        : base(reason)
    {
    }

    public PairWriteRefusedException()
    {
    }

    public PairWriteRefusedException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}
