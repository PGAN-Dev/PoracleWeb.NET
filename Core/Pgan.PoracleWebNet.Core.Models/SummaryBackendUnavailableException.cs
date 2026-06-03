namespace Pgan.PoracleWebNet.Core.Models;

public class SummaryBackendUnavailableException : Exception
{
    public SummaryBackendUnavailableException() : base("Quest summary service unavailable.") { }
}
