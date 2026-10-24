namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>An <c>account_links</c> row holds a value this version cannot read, such as an unknown default destination.</summary>
public class AccountLinkUnreadableException(int linkId, string message) : Exception(message)
{
    public int LinkId { get; } = linkId;
}
