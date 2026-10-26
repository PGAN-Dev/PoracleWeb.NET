---
paths:
  - "**/*TestAlert*"
  - "**/*test-alert*"
  - "**/TestAlerts/**"
  - "**/*TestPayload*"
---

## Test Alerts
- `POST /api/test-alert/{type}/{uid}` triggers a test notification for a specific alarm. Supported types: `pokemon`, `raid`, `egg`, `quest`, `invasion`, `lure`, `nest`, `gym`.
- **Parallel data fetch**: `TestAlertService` uses `Task.WhenAll` to fetch the alarm (via `IPoracleTrackingProxy`) and the human record (via `IPoracleHumanProxy`) concurrently.
- **Mock webhook payloads**: The service builds a realistic mock webhook payload per alarm type using the alarm's filter fields (e.g., pokemon_id, raid_level, quest_reward). The user's location from the human record is used as the mock event coordinates.
- **PoracleNG test endpoint**: The built payload is sent to PoracleNG's `POST /api/test` endpoint, which formats and delivers the notification to the user via their configured webhook.
- **Rate limiting**: `test-alert` policy at 5 requests per 60s, partitioned per person (per IP only when there is no token). An impersonation session shares the impersonator's bucket (`TestAlertPartitionKey`, #1025). See "Rate Limiting".
- **Controller validation**: The `type` path parameter is validated against a hardcoded set of valid alarm types before calling the service.
- **Frontend**: `TestAlertService` (Angular, `core/services/test-alert.service.ts`) tracks per-UID cooldowns (15s) client-side and deduplicates in-flight requests. Displays success/error/cooldown feedback via snackbar. Test button appears in the card actions of the eight types that support it (not max battles, fort changes or Pokéstop events). `/api/test-alert` is in the error interceptor's silent list, so the service's own message is the only one shown and it has to cover every status (429, 404, 501's server reason, 502 with PoracleNG's reason as `TEST_ALERT.REFUSED`, the feature-disabled 403, anything else as `TEST_ALERT.FAILED`).
- **A refused `/api/test` is a 502 carrying PoracleNG's reason.** `PoracleApiProxy.SendTestAlertAsync` reads the body of a non-2xx ("message delivery not configured - check Discord/Telegram token settings") and throws `PoracleRequestRefusedException` with status 502; `TestAlertController` passes on only that status (`when StatusCode == 502`). A refusal from reading the alarm or the human carries its own 400/404/409 and still answers the generic 500, so a 404 there never reads as "alarm not found".
