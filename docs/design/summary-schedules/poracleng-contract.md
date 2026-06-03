# PoracleNG `summary_schedules` — verified API contract

> Ground truth fetched from `jfberry/PoracleNG@main` on 2026-06-03 via `gh api`.
> Source files: `API.md` (Summary Schedules section), `processor/internal/db/migrations/000003_summary_schedules.{up,down}.sql`,
> `processor/internal/store/summary_sql.go`, `processor/cmd/processor/summary_scheduler.go`.

## Correction to issue #292's guess

Issue #292 referenced `/api/summary-schedules`. The **actual** endpoints are under **`/api/summaries/{id}`**, and there are **five** of them (including a force-flush `/trigger`).

## Endpoints (all require `X-Poracle-Secret` header)

| Method | Path | Purpose | Notes |
|---|---|---|---|
| GET | `/api/summaries/{id}` | List every schedule for the user, across alert types | `{status:"ok", schedules:[{id, alert_type, active_hours}]}` |
| GET | `/api/summaries/{id}/{alertType}` | Get one schedule | `404 {status:"error",message:"schedule not found"}` when missing |
| POST | `/api/summaries/{id}/{alertType}` | Create or replace | Body `{active_hours: [...] OR "stringified"}`. Canonicalised before storage. Triggers a debounced state reload. |
| DELETE | `/api/summaries/{id}/{alertType}` | Remove | Deleting a missing schedule is a **no-op `200 ok`** (idempotent) |
| POST | `/api/summaries/{id}/{alertType}/trigger` | Force-flush buffered events now | Always `200 ok`. Equivalent to `!summary <alertType> now`. **Synchronous flush-and-deliver**: `DispatchQuestSummary` re-enriches buffered quests, renders, delivers via the dispatcher, and clears the bucket. No-ops on an empty buffer. (A 503 here is an init fault, not "feature off" — see below.) |

- `{id}` = user's Discord/Telegram ID.
- `{alertType}` = **`quest` only** today (the only renderer wired). Future types would add constants.

## Feature gate — IMPORTANT correction (verified against `main.go:450-459`)

The API.md "503 lets clients distinguish feature-off" line is **misleading**. Verbatim from `processor/cmd/processor/main.go`:

> *"The endpoints are always live — `quest_summary_enabled` only gates the scheduler tick / matcher routing, not the schedule storage, so operators can prepare schedules in advance and flip the feature flag without losing data. Handlers return 503 only when the underlying store wasn't constructed (an early-init failure), not when the feature flag is off."*

Consequences for PoracleWeb:

- A raw `503` from these endpoints means a **server/init fault (outage)** — treat it as a transient "try again" banner, **never** as "feature off".
- With `quest_summary_enabled = false`, **all five endpoints still serve 200/404 normally and schedules persist.**
- PoracleWeb therefore **cannot learn "feature off" from these endpoints.** It must read `tracking.quest_summary_enabled` from the **config proxy** (`IPoracleApiProxy` → `GET config`) and surface it as a **200-body boolean** (mirror Golbat / `PokemonAvailabilityController`'s `enabled:false`), ideally folded into `auth/me` and `IMemoryCache`-cached 5-min like `SiteSettingService`.

## Data shape

`summary_schedules` table:

```sql
CREATE TABLE IF NOT EXISTS `summary_schedules` (
  `id`           varchar(64)   NOT NULL,         -- human Discord/Telegram id
  `alert_type`   varchar(32)   NOT NULL,         -- 'quest' today
  `active_hours` varchar(4096) NOT NULL DEFAULT '[]',
  PRIMARY KEY (`id`,`alert_type`)
) ENGINE=InnoDB;
```

- **Keyed by `(id, alert_type)` → per-USER, NOT per-profile.** A user has at most one quest-summary schedule, shared across all their profiles. (Contrast: the alarm `clean` summary bit is per-alarm/per-profile; the *schedule* is per-user.)
- `active_hours` is **the exact same shape as a profile's `active_hours`**: a JSON array of `[{day:1-7, hours:0-23, mins:0-59}]`. Stored verbatim (`Set` upserts the JSON string as-is via `INSERT ... ON DUPLICATE KEY UPDATE`).
- Store ops: `Get` returns nil when no row; `Set` upserts; `Delete` ignores missing; `ListByType` lists all for a type.

## Scheduler behavior (context, not directly wired by the UI)

- `SummaryScheduler` wakes at the same wall-clock minute marks as the profile scheduler, walks each user's per-`alertType` schedule, and dispatches a grouped summary when the schedule matches the user's **local time**.
- Local time uses the human's lat/lon; **`0/0` falls back to `[general] default_timezone`** → same "missing location → wrong timezone" hazard the profile active-hours UI already warns about via `LocationWarningComponent`.
- Buffered entries are swept (`quest_summary_buffer_ttl_hours`) so stale quests are dropped if the schedule never fires.

## Implications for the PoracleWeb UI feature

1. The schedule is **just an `active_hours` array** keyed by the current user + `quest`. PoracleWeb already has the full active-hours stack (editor dialog, utils, models, validation, location warning) — the feature is mostly reuse.
2. Surface is **per-user**, not per-profile — do not scope it by `profile_no`.
3. Capability ("is quest summary on?") comes from the **config flag**, not a 503. Surface it as a 200-body boolean and hide/disable the UI off that (mirror Golbat's "hidden when unconfigured" pattern). A 503 is an outage banner only.
4. A **"Send summary now"** action maps to the `/trigger` endpoint.
5. Pairs with the quest **"Daily summary"** alarm toggle shipped in PR #295: enabling the bit only matters once a schedule exists — link the two in the UX.
