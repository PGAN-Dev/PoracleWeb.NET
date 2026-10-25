namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>A Discord or Telegram id is already in another link.</summary>
public class AccountLinkConflictException : Exception
{
    public AccountLinkConflictException(string message, LinkSide? side = null, Exception? inner = null)
        : base(message, inner) => this.Side = side;

    /// <summary>Which id collided, when the database said. Null when it could not be told.</summary>
    public LinkSide? Side
    {
        get;
    }
}
