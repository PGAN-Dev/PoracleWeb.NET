namespace Pgan.PoracleWebNet.Core.Models;

public class SummarySchedule
{
    public string AlertType { get; set; } = "quest";

    // Raw JSON array literal; "[]" when cleared. Never project the upstream "id" (it is the user id — IDOR leak).
    public string ActiveHours { get; set; } = "[]";
}

public class SummaryScheduleRequest
{
    // Accepts the SPA's JSON.stringify(entries); null/whitespace = clear.
    public string? ActiveHours
    {
        get; set;
    }
}
