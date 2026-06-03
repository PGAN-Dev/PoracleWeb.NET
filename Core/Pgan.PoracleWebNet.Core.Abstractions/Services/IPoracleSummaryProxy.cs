using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

public interface IPoracleSummaryProxy
{
    // GET /api/summaries/{id} -> unwraps { "schedules":[...] }. Returns the schedules JsonElement (array), or null on non-success.
    Task<JsonElement?> GetSchedulesAsync(string userId);

    // GET /api/summaries/{id}/{alertType} -> unwraps { "schedule":{...} }. Returns null on 404.
    Task<JsonElement?> GetScheduleAsync(string userId, string alertType);

    // POST /api/summaries/{id}/{alertType}; body { "active_hours": <raw JSON array literal> }. Upsert.
    // activeHoursJson is an ALREADY-VALIDATED raw JSON array literal ("[]" or "[{...}]").
    Task SetScheduleAsync(string userId, string alertType, string activeHoursJson);

    // DELETE /api/summaries/{id}/{alertType} -- idempotent (200 on missing).
    Task DeleteScheduleAsync(string userId, string alertType);

    // POST /api/summaries/{id}/{alertType}/trigger -- synchronous flush-and-deliver, always 200.
    Task TriggerAsync(string userId, string alertType);
}
