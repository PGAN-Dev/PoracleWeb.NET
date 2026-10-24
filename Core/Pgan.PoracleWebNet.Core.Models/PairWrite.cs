namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>Why the other side did not take a write. The client translates the code; the reason is the English fallback.</summary>
public static class PairWarningCodes
{
    /// <summary>PoracleNG refused it with a sentence of its own, carried as the reason.</summary>
    public const string Refused = "refused";

    /// <summary>PoracleNG could not be reached for that side.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>The two sides' profiles no longer line up, so the other side was left alone.</summary>
    public const string OutOfStep = "out_of_step";

    /// <summary>PoracleNG refused to rename an area on that side.</summary>
    public const string RenameRefused = "rename_refused";
}

/// <summary>The other side of a linked pair did not take a write the signed-in side already took.</summary>
public sealed record PairWarning(LinkSide Side, string Reason, string Code = PairWarningCodes.Refused)
{
    /// <summary>The shape a response carries as <c>pairWarning</c>.</summary>
    public PairWarningBody ToBody() => new(AccountLinkKeys.Wire(this.Side), this.Reason, this.Code);
}

/// <summary>Wire form of <see cref="PairWarning"/>: <c>{ side: "telegram", reason, code }</c>.</summary>
public sealed record PairWarningBody(string Side, string Reason, string Code);

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

    public PairWriteRefusedException(string reason, string code)
        : base(reason) => this.Code = code;

    public PairWriteRefusedException()
    {
    }

    public PairWriteRefusedException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }

    /// <summary>The <see cref="PairWarningCodes"/> entry the warning carries.</summary>
    public string Code { get; } = PairWarningCodes.Refused;
}
