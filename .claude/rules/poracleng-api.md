---
paths:
  - "**/Poracle*"
  - "**/IPoracle*"
  - "**/Tracking*"
  - "**/MasterData*"
  - "**/*Proxy*"
  - "**/Pgan.PoracleWebNet.Api/Controllers/**"
---

## PoracleNG API Proxy Layer
- **All alarm CRUD** (Monster, Raid, Egg, Quest, Invasion, Lure, Nest, Gym) is proxied through PoracleNG's REST API via `IPoracleTrackingProxy`. Services no longer use repositories or direct DB writes for alarm operations.
- **Human/Profile management** is proxied through `IPoracleHumanProxy` for single-user operations (get, create, start/stop, set location, set areas, switch profile). Admin bulk operations (get all users, the webhook list, delete user) use PoracleNG's `GET /api/v2/humans` and `DELETE /api/v2/humans/{id}` through the same proxy.
- Both proxies are registered via `AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>()` and `AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>()`.
- Authentication uses the `X-Poracle-Secret` header on every request (from `Poracle:ApiSecret` config).
- **Snake_case JSON**: Proxy classes use `JsonNamingPolicy.SnakeCaseLower` for serialization/deserialization, matching PoracleNG's wire format.
- **`?silent=true`**: Tracking create requests append `?silent=true` to suppress Discord confirmation messages from PoracleNG.
- **`TrackingCreateResult`**: PoracleNG returns `{ newUids, alreadyPresent, updates, insert }` from creates. The `TrackingCreateResult` record captures this so services can assign the UID back to the created model.
- **HumanService is proxy-first**: All single-user ops (`GetByIdAsync`, `ExistsAsync`, `CreateAsync`, `DeleteAllAlarms`, `StartAsync`, `StopAsync`, `SetLocationAsync`) go through `IPoracleHumanProxy` with no DB fallback -- proxy errors propagate to the caller. `GetHumanAsync` (and so `GetByIdAsync`/`ExistsAsync`/`GetAreasAsync`) answers null only on a 404 and throws for any other non-2xx, the `GetAdminRolesAsync` contract: answering null for every failure made an outage read as a deleted account (the purge's "already gone" 404, `/api/auth/me` signing live users out). A caller that re-reads after a write that already landed catches the throw and falls back, as it did on null (#994). Admin bulk ops (`GetAllAsync`, `DeleteUserAsync`) go through the proxy's v2 list and delete too; `HumanRepository` no longer exists.
- **ProfileService is proxy-first**: Single-user reads (`GetByUserAsync`, `GetByUserAndProfileNoAsync`) and all CRUD go through `IPoracleHumanProxy`. No DB fallback -- proxy errors propagate.
- **PoracleNG handles**: field defaults (template, PVP, size, etc.), dedup detection, immediate state reload on every mutation, grunt_type normalization, saving the active profile's areas on a profile switch.

## Keep the PoracleNG Checkout Pinned To What Prod Runs

`E:/PGAN/pogogit/PoracleNG` drifts. On 2026-08-08 it was four months behind prod, and its `DiffTracking` lacked the `totalDiffs == 1` clause entirely — reading it produced three wrong fixes in one day.

PoracleNG has no version endpoint (`/health` only). To establish what prod runs: `ss -lntp | grep 3030` for the pid, `/proc/<pid>/exe` for the binary, and `git -C /source/PoracleNG rev-parse HEAD`; compare the binary mtime against the commit date. Then check out that commit locally.

**Even then, verify against the live instance.** POST directly to `/api/tracking/<type>/<id>?silent=true` with the `X-Poracle-Secret` header and read the row back. Source and running binary have disagreed before, and a unit test cannot tell you which is right.

## Minimum PoracleNG Is 5.4.0

`PoracleServerProfile.MinimumSupported` is 5.4.0, since 2026-10-21 (#996). jfberry/PoracleNG#328 answered
every request this project had open upstream, and "no PoracleNG database access" is only reachable by
deleting the fallbacks for older servers rather than keeping them beside the v2 paths. The first raise
(5.1.0 to 5.2.1, 2026-10-20) deleted the lure/invasion natural-key refusals and delete-and-recreate edit
(`INaturalKeyCapabilityService`, `NaturalKeyTrackingUpdate`), `PoracleSchemaVersionReader` (the last read
of PoracleNG's `schema_migrations` and `information_schema`), the separate `general.disable_fort_update`
read, and every per-feature `>= 5.2.0` compare. The second deleted, for humans, profiles, areas listing,
places, mutes and geocoding: `HumanRepository` and `ProfileRepository`, `ProfileNumbering`'s list-and-diff,
the v1 branch of every single-human call and its per-route "v2 absent" flags,
`MuteCapabilityService`, `PlaceUpdateCapabilityService`, `QuestPokecoinCapabilityService` and
`CostumeCapabilityService` (with `GET /api/quests/capability` and `GET /api/settings/costume-capability`
and the SPA's `canEdit`/`pingSupported`/capability conditionals), `ProbePokestopEventsAsync`, the legacy
geocoder call, `Poracle:TrackingApiVersion`, and every `PoracleV2Capabilities` flag except three.
`InvasionGruntType` and `Read` survive too, set but never read (`TrustedSetAreas` went with the area writes). What stayed
is anything covering a newer build or fork. `PoracleContext`, `pweb_settings` and the `DB_*` bridge went in the same release (PoracleWeb.NET no longer connects to Poracle's database).

**One rule decides version-gated features: `PoracleServerProfile.IsSupported`** — PoracleNG answered and
is not known to be older than the minimum. It now decides little beyond whether the v2 tracking surface is
tried, because nothing else is optional. An unparseable version, or the `0.0.0` a locally built binary
reports, counts as current; an unreachable server does not. Anything newer than 5.4.0 is gated on the
server's own `/openapi.json` (`IPoracleV2SchemaService`), never on a version compare.

**A route a too-old server lacks is an error, not a fallback.** A plaintext `404 page not found` from a
v2 human, profile, area, place or geocode route throws `PoracleUnsupportedException(feature,
"PoracleNG 5.4.0")` (409 through `PoracleUnsupportedExceptionFilter`). A problem+json 404 keeps its
meaning: not found. Webhook ids in those paths go through `Uri.EscapeDataString`. When PoracleNG cannot be
reached at all, `PoracleUnreachableExceptionFilter` answers 503 on the Mute and Location
controllers and on all eleven alarm controllers (Pokemon, raid, egg, quest, invasion, Pokestop event, lure, nest,
gym, max battle, fort change), where a failed read used to look like an empty list.

**Older servers fail open, loudly.** `PoracleCompatibilityStartupService` logs an error at startup naming
what breaks, and the admin Versions card shows `belowMinimum`; the app keeps running, because refusing to
start would take the whole site down over something an operator fixes in minutes. Raising the floor again
follows the same rule: delete what only the old floor needed, keep what covers newer builds and forks.

## PoracleNG API Availability
The PoracleNG REST API (`Poracle:ApiAddress`) must be running and reachable for all alarm, human, profile, and area operations. If the API is down: alarm CRUD, human lookups, profile reads/writes, location updates, area updates, and profile switches all fail with no DB fallback. PoracleWeb.NET has no connection to Poracle's database. Monitor PoracleNG uptime as a hard dependency.

## PoracleNG Response Wrappers
PoracleNG wraps its API responses in container objects. Human data is returned as `{"human": {...}}` and profile lists as `{"profile": [...]}`. The proxy classes (`PoracleHumanProxy`) must extract the inner object/array before deserializing to model types. Failing to unwrap causes deserialization to return null/empty results.

## uid:0 in Create Requests
When creating new alarms, the model's `uid` defaults to `0`. A v2 rule body has no place for `uid`, `id`, `profile_no` or `description`, so the translator drops them and a create can't be mistaken for an update.

## PoracleNG active_hours String Types
PoracleNG stores `hours` and `mins` fields in `active_hours` JSON entries inconsistently -- sometimes as numbers, sometimes as strings (e.g., `"hours": "8"` instead of `"hours": 8`). The frontend `parseActiveHours` utility applies `Number()` coercion when deserializing to ensure consistent numeric types. The backend `ValidateActiveHours` method in `ProfileController` accepts both `JsonValueKind.Number` and `JsonValueKind.String` (parsing strings via `int.TryParse`) for the same reason.
