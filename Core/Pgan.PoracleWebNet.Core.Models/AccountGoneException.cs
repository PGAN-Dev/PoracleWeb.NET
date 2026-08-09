namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The account the request is authenticated as no longer exists.
/// </summary>
/// <remarks>
/// A JWT outlives the account it names. After an admin deletes a user, PoracleNG answers 404 "User not
/// found" for every lookup, and <c>EnsureSuccessStatusCode</c> turned that into an unhandled 500 — so the
/// deleted user sat in a fully rendered app throwing "an unexpected error occurred" on every page, because
/// the SPA only signs out on 401. <c>/api/auth/me</c> alone got this right (#545). Raised here and mapped to
/// 401 so every endpoint answers the same way and the session ends. See #584.
/// </remarks>
public sealed class AccountGoneException : Exception
{
    public AccountGoneException()
        : base("This account no longer exists.")
    {
    }

    public AccountGoneException(string message)
        : base(message)
    {
    }

    public AccountGoneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
