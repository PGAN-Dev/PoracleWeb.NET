# Quest Summary Schedules — Engineering Design

> **Status:** Proposed
> **Companion issue:** Quest summary delivery scheduling UI
> **Scope:** Add a per-user "Quest summary delivery" scheduling surface to PoracleWeb, proxied to PoracleNG's `summary_schedules` API. Pairs with — but does not modify — the quest "Daily summary" clean-bit-4 toggle already shipped in #295.
> **Worktree root:** `E:\PGAN\pogogit\PGAN.Poracle.Web\.claude\worktrees\feat+summary-schedule-ui\`
> All paths and line anchors in this document were verified against the files in that worktree and against `jfberry/PoracleNG@main` (fetched 2026-06-03).

---

## 1. Summary

PoracleNG can deliver matched quests as a single batched daily summary instead of one DM per quest. Whether a user receives a summary, and at what times, is governed by a per-user `summary_schedules` row keyed by `(id, alert_type)`. The schedule's `active_hours` is the **same** `[{day,hours,mins}]` shape PoracleWeb already edits for profile active-hours.

This feature gives the user a UI to **view, edit, clear, delete, and force-deliver** their quest summary schedule, reusing the existing active-hours editor dialog, location-warning component, and active-hours validator with **zero new editor code**. The surface is a dialog launched from the Quests page toolbar — **no new nav item, no new route**.

### 1.1 What this design deliberately does NOT do

These were evaluated and cut (see §10, Simplification Decisions):

- **No `JsonNamingPolicy.SnakeCaseLower` in the proxy.** `active_hours` is passed through as raw JSON. The "PoracleHumanProxy uses snake_case" claim in CLAUDE.md is factually wrong.
- **No capability inference from a 503.** A 503 from `/api/summaries/*` is an outage, not "feature off." Capability is a config-derived boolean in a 200 body (Golbat pattern).
- **No new feature-gate key / route / nav item.** The panel inherits `disable_quests` from the existing `/quests` route gate.
- **No changes to the clean-bit-4 SUMMARY toggle** (shipped in #295). Only a hint-text annotation is added.

---

## 2. Verified upstream contract (PoracleNG `summary_schedules`)

Verified against `jfberry/PoracleNG@main` source: `processor/internal/api/summary.go`, `summary_test.go`, `tracking.go`, `cmd/processor/main.go`, `helpers.go`, `quest_summary_dispatch.go`, `tracker/summary_buffer.go`, `db/migrations/000003_summary_schedules.up.sql`.

### 2.1 Endpoints

All five live under `/api/summaries`, behind the same `X-Poracle-Secret` middleware (`main.go:461-466`). `{id}` is the Discord/Telegram user id. `{alertType}` is `quest`-only today.

| Method / Path | Request | Success | Notable errors |
|---|---|---|---|
| `GET /api/summaries/{id}` | — | `200 {"status":"ok","schedules":[{id,alert_type,active_hours[]}]}` (empty `[]` when none) | 500 db |
| `GET /api/summaries/{id}/{alertType}` | — | `200 {"status":"ok","schedule":{id,alert_type,active_hours[]}}` | **404** not found; 400 unknown type; 500 db |
| `POST /api/summaries/{id}/{alertType}` | `{"active_hours":[{day,hours,mins}]}` **or** stringified | `200 {"status":"ok"}` (upsert; debounced reload) | 400 missing/invalid/unparseable `active_hours`; 400 unknown type; 500 db |
| `DELETE /api/summaries/{id}/{alertType}` | — | `200 {"status":"ok"}` (idempotent; debounced reload) | 400 unknown type; 500 db |
| `POST /api/summaries/{id}/{alertType}/trigger` | — | `200 {"status":"ok"}` (synchronous flush-and-deliver) | 400 unknown type; 503 only if dispatch nil (init fault) |

### 2.2 Storage & keying

Table (`000003_summary_schedules.up.sql`):

```sql
summary_schedules(
  id           varchar(64)   NOT NULL,
  alert_type   varchar(32)   NOT NULL,
  active_hours varchar(4096) NOT NULL DEFAULT '[]',
  PRIMARY KEY(id, alert_type)
) ENGINE=InnoDB
```

- **Per-`(id, alert_type)`, per-USER — NOT per-profile.** No `profile_no` column. A single Discord/Telegram id is the entire blast radius.
- **No FK CASCADE** — cleanup is explicit in PoracleNG's human-delete paths. PoracleWeb does not own this table.
- `active_hours` is the identical `[{day:1-7, hours:0-23, mins:0-59}]` shape as a profile's `active_hours`; both flow through PoracleNG's `db.ParseActiveHours`, enforcing structural compatibility server-side.

### 2.3 Envelope

- Success: `{"status":"ok", ...data}`. Bare success bodies for POST/DELETE/trigger are `{"status":"ok"}` only.
- Error/503: `{"status":"error","message":...}` (same envelope at every error code).
- **Read-side `active_hours` is always a JSON array literal** (`json.RawMessage`), defaulting to `[]`. The array-vs-stringified duality is a **write-side accommodation only** — reads always return canonical array form.
- **Write-side:** POST accepts `active_hours` as an array/object (re-`Marshal`'d) or a pre-stringified string (stored verbatim). Both validated by `db.ParseActiveHours` → 400 on invalid JSON. `""`/`"[]"`/`"{}"` are accepted as "clear schedule without deleting the row."

### 2.4 Reload & trigger semantics

- **POST and DELETE trigger a debounced reload:** `triggerReload` (`helpers.go:382`) = `time.AfterFunc(500ms)`, prior timer cancelled under mutex. Rapid writes coalesce into one reload ~500 ms after the last. **GET and trigger do not reload.**
- **`trigger` is flush-and-deliver-NOW, synchronously.** `HandleSummaryTrigger` calls `DispatchQuestSummary`, which re-enriches the already-buffered quests, renders, **delivers via the delivery dispatcher, and clears the bucket**. Always returns 200; no-ops on an empty buffer.

### 2.5 Corrected contract errors (load-bearing — do NOT inherit the issue's wording)

| Issue claim | Verified reality | Source |
|---|---|---|
| "Feature gate → all five return 503 when `quest_summary_enabled=false`; UI must hide on 503." | **WRONG.** The HTTP surface is wired **unconditionally** (`main.go:135, 456-459`). The flag (`main.go:216`) only gates the *scheduler tick*, not the endpoints. With the flag off, all five endpoints serve normally and schedules persist. A real 503 fires **only** on a nil-deps early-init store fault (an outage), proven by `TestSummaryAPI_FeatureDisabled503` forcing 503 via `&SummaryDeps{}`, not config. | `main.go:135,216,450-459`; `summary_test.go:180-198` |
| "UI must hide/disable when it sees 503." | **Unsafe.** PoracleWeb cannot learn "feature off" from these endpoints. Capability must come from `tracking.quest_summary_enabled` via the config proxy, surfaced as a **200-body boolean**. Treat a raw 503 as a transient retry banner. | as above |
| Trigger "(enqueues, not delivers)". | **INVERTED.** Trigger is *flush-and-deliver-now*. The "matched-but-not-delivered" semantics belong to the matcher → `summary_buffer`, not the trigger. | `quest_summary_dispatch.go` |
| "Only 404/503/200 codes." | PoracleNG also returns **400** (unknown alert type; `missing path/id parameter`; `active_hours must be specified`; `invalid/empty request body`; unparseable `active_hours`) and **500** (`database error`). 404 is GET-one-only. Note: `GET /api/summaries/{id}` (list) does NOT validate alertType — it iterates known types internally. | `summary.go` |

### 2.6 Unverified (flagged, non-blocking)

- The local-time / `0,0 → default_timezone` scheduler hazard and the exact equivalence to profile `active_hours` were not re-read from scheduler internals. Both go through `db.ParseActiveHours`, so structural compatibility is enforced; the timezone behavior is plausible but unconfirmed. We still surface the same `LocationWarningComponent` the profile UI uses, as defense.

---

## 3. Integration map (file anchors)

| Concern | Anchor | How it's used |
|---|---|---|
| Proxy shape to mirror | `Core\...\Core.Services\PoracleHumanProxy.cs` — primary ctor reads `configuration["Poracle:ApiAddress"]`/`["Poracle:ApiSecret"]` (8-12); `Encode` = `Uri.EscapeDataString` (18); `SendAsync(method, path, body?)` adds `X-Poracle-Secret` only when non-empty (139-153); unwrap via `JsonDocument` + `TryGetProperty(...).Clone()` (28-37). | New `PoracleSummaryProxy` copies this verbatim. **FLAG:** no `SnakeCaseLower` policy here — pass `active_hours` as raw JSON. |
| `System.Net` import for 503 branch | `Core\...\Core.Services\PoracleTrackingProxy.cs:1` | Available for `HttpStatusCode.ServiceUnavailable` checks. |
| JWT user id | `Applications\...\Api\Controllers\BaseApiController.cs:13` — `protected string UserId => User.FindFirstValue("userId")` | New controller derives `BaseApiController`, uses `this.UserId` only; **no `{userId}` route segment**. |
| Validation to reuse | `Applications\...\Api\Controllers\ProfileController.cs:182` — `internal static (bool IsValid, string? Error) ValidateActiveHours(string?)`; helper `TryGetIntValue` (247-262) accepts Number + String kinds. Already targeted by `ActiveHoursValidationTests`. | Extract to shared static `ActiveHoursValidator`; both controllers call it. |
| Active-hours utilities (frontend) | `Applications\...\ClientApp\src\app\core\models\active-hours.models.ts:1-120` — `parseActiveHours`, `groupActiveHours`, `formatTime12h`, `compressDayRange`, `ActiveHourEntry`, `DAY_LABELS`. | **FLAG:** there is **no** `active-hours.utils.ts`. Import from `active-hours.models.ts`. |
| Editor dialog to reuse | `Applications\...\shared\components\active-hours-editor-dialog\active-hours-editor-dialog.component.ts:20-49,131-133` — `MAT_DIALOG_DATA = { activeHours: ActiveHourEntry[]; profileColor?; profileName }` (profileName **mandatory**); `save()` → `dialogRef.close(this.entries())` returns `ActiveHourEntry[]`; cancel/clear → `undefined`. | Open with `{ activeHours: parseActiveHours(...), profileName: <translated alert label> }`; on a returned array, `JSON.stringify` and PUT. |
| Location warning to reuse | `Applications\...\shared\components\location-warning\location-warning.component.ts:14-19` — signal inputs `hasActiveHours`/`latitude`/`longitude`; `show = hasActiveHours && lat===0 && lon===0`. | Drop in directly. |
| User location source | `core\services\location.service.ts:getLocation()` → `Location { latitude, longitude }` | Feeds the warning. No lat/lon on `AuthService`/`UserInfo`. |
| Quests module entry point | `Applications\...\modules\quests\quest-list.component.ts:26-63` — standalone, OnPush, already imports `MatDialog` (injected line 50), `MatMenuModule`, `MatButtonModule`, `MatTooltipModule`. | Add a `mat-menu` item in `quest-list.component.html` that opens the new dialog, gated on capability. |
| Clean-bit hint (annotate only) | `quest-add-dialog.component.html:115/116`, `quest-edit-dialog.component.html:88/89` — `QUESTS.SUMMARY_MODE` + `QUESTS.SUMMARY_HINT`; bit logic `isSummary(clean)` (`quest-list.component.ts:254`). | Append `QUESTS.SUMMARY_DISABLED_HINT` when capability off. **Do not touch bit logic (#295/#292).** |
| Routing / gating | `app.routes.ts:47-49` — `path:'quests'`, `canActivate:[authGuard, disabledFeatureGuard('disable_quests')]`. | Panel inherits `disable_quests`. No new route. |
| Capability pattern (backend) | `PokemonAvailabilityController.cs:7-29` returns `{ available:[], enabled:false }` in a 200 when the service is null. Golbat conditional DI at `ServiceCollectionExtensions.cs:107-114`. | Mirror: capability is a 200-body boolean, never a 503. |
| Capability pattern (frontend) | `core\services\pokemon-availability.service.ts:12,33-56` — `enabled = signal(false)`, fetch-once `loaded` guard, 5-min `setInterval`, `catchError` keeps prior value. | Mirror exactly. |
| Cache precedent | `Core\...\Core.Services\SiteSettingService.cs:27-34,69-80` — `CacheTtl = 5min`, `TryGetValue`→fetch→`Set`, caches null/negative answers; `IMemoryCache` ctor-injected, globally registered (`ServiceCollectionExtensions.cs:31`). | Cache the capability boolean (including `false`) for 5 min. |
| DI registration | `ServiceCollectionExtensions.cs:117-123` (typed-client `AddHttpClient<T,T>` block); scoped services 82-89. | Add `AddHttpClient<IPoracleSummaryProxy, PoracleSummaryProxy>()` after 123; capability service scoped near 82-89. |
| Rate-limit policy | `TestAlertController.cs:9` `[EnableRateLimiting("test-alert")]`; `Program.cs:379-385` partitions `user:{userId}`→IP, 5/60s. | Apply `test-alert` policy to the trigger action. |
| i18n locales | `Applications\...\ClientApp\src\assets\i18n\` — 11 files: `da, de, en, es, fr, it, nl, pl, pt, pt-BR, sv`. | Add new keys to all 11. |

---

## 4. Backend design

### 4.1 Key decisions (grounded)

1. **Capability is NOT a 503.** All five endpoints serve normally regardless of `quest_summary_enabled` (§2.5). Capability ("does this PoracleNG support quest summaries / is it enabled") is read from `tracking.quest_summary_enabled` via the config proxy and surfaced as a **200-body boolean** (Golbat pattern). A 503 from data endpoints is a **transient outage** → a banner, never a panel-hide.
2. **Two distinct "off" sources, two distinct mechanisms:**
   - `disable_quests` site setting → handled by `[RequireFeatureEnabled(DisableFeatureKeys.Quests)]` on the controller (403 with `disableKey` body the SPA interceptor redirects on).
   - PoracleNG quest summary not enabled → `enabled:false` from the capability boolean → SPA hides the panel + annotates the clean-bit hint.
3. **User id is ALWAYS the JWT claim.** Routes carry no `{userId}` segment; the controller passes `this.UserId` to every proxy call. `alertType` validated against a `quest`-only allow-set.

### 4.2 `IPoracleSummaryProxy` / `PoracleSummaryProxy`

**New files:** `Core\...\Core.Abstractions\Services\IPoracleSummaryProxy.cs`, `Core\...\Core.Services\PoracleSummaryProxy.cs`.

Mirror `PoracleHumanProxy` exactly: primary ctor `(HttpClient, IConfiguration)`, `_apiAddress`/`_apiSecret` fields, `Encode`, and a copy of `SendAsync(HttpMethod, string path, string? body = null)` (header added only when secret non-empty).

```csharp
public interface IPoracleSummaryProxy
{
    // GET /api/summaries/{id} -> unwraps { "schedules":[...] }; null/empty array when none.
    Task<JsonElement?> GetSchedulesAsync(string userId);
    // GET /api/summaries/{id}/{alertType} -> unwraps { "schedule":{...} }; null on 404.
    Task<JsonElement?> GetScheduleAsync(string userId, string alertType);
    // POST /api/summaries/{id}/{alertType}; body { "active_hours": <raw JSON array literal> }. Upsert.
    Task SetScheduleAsync(string userId, string alertType, string activeHoursJson);
    // DELETE /api/summaries/{id}/{alertType} -- idempotent (200 on missing).
    Task DeleteScheduleAsync(string userId, string alertType);
    // POST /api/summaries/{id}/{alertType}/trigger -- synchronous flush-and-deliver, always 200.
    Task TriggerAsync(string userId, string alertType);
}
```

Implementation notes (verified against `PoracleHumanProxy`):

- **Raw-JSON pass-through, no naming policy.** The only POST field is snake_case `active_hours`; the proxy writes it literally:
  ```csharp
  var body = $"{{\"active_hours\":{(string.IsNullOrWhiteSpace(activeHoursJson) ? "[]" : activeHoursJson)}}}";
  ```
  The caller hands the proxy an already-validated JSON array literal (`"[]"` or `"[{...}]"`).
- **Unwrap:** `JsonDocument.Parse` → `TryGetProperty("schedules"/"schedule", out var x) ? x.Clone() : root.Clone()`. `GetScheduleAsync` returns `null` on `HttpStatusCode.NotFound`.
- **503 handling.** Branch on `response.StatusCode == HttpStatusCode.ServiceUnavailable` **before** `EnsureSuccessStatusCode()` and throw a dedicated `SummaryBackendUnavailableException` so the controller maps it to a transient 503 (not a 403, not a panel-hide). Reads guard with `IsSuccessStatusCode` after the 503 branch.

**New exception:** `Core\...\Core.Models\SummaryBackendUnavailableException.cs` (plain `Exception` subtype in `Core.Models` so the controller and a global filter reference it without an Api→Services leak).

> **Note:** Capability is computed by the capability service from the **config proxy**, not by inferring it from a proxy 503. The proxy's 503 path is purely an outage signal.

### 4.3 `SummaryScheduleController`

**New file:** `Applications\...\Api\Controllers\SummaryScheduleController.cs`, inherits `BaseApiController` (gets `[ApiController]`, `[Authorize]`, `UserId`).

```csharp
[Route("api/summary-schedules")]
[RequireFeatureEnabled(DisableFeatureKeys.Quests)]   // mirrors QuestController.cs:10
public class SummaryScheduleController(
    IPoracleSummaryProxy summaryProxy,
    ISummaryCapabilityService capability) : BaseApiController
```

REST surface (no `{userId}` anywhere; id is always `this.UserId`):

| Verb / Path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/summary-schedules/capability` | — | `200 { enabled: bool }` | never 5xx — degrades to `false` |
| `GET /api/summary-schedules` | — | `200` (schedules array, unwrapped) | 403 (disable_quests); 503 (transient) |
| `GET /api/summary-schedules/{alertType}` | — | `200 SummarySchedule` | 404 (no schedule); 400 (bad type); 403; 503 |
| `PUT /api/summary-schedules/{alertType}` | `{ activeHours: "<json>" }` | `204 NoContent` (upsert) | 400 (validation / bad type); 403; 503 |
| `DELETE /api/summary-schedules/{alertType}` | — | `204 NoContent` (idempotent) | 400 (bad type); 403; 503 |
| `POST /api/summary-schedules/{alertType}/trigger` | — | `204 NoContent` | 400 (bad type); 403; 503 |

- **PUT (not POST) for set** — PoracleWeb's own surface uses PUT for idempotent-upsert semantics; the proxy still issues PoracleNG's upstream POST.
- **`alertType` allow-set:** `private static readonly HashSet<string> ValidAlertTypes = new(StringComparer.OrdinalIgnoreCase){ "quest" }`, checked at the top of every `{alertType}` action; unknown → `BadRequest`. Mirrors `TestAlertController.ValidTypes`.
- **Validation reuse:** PUT calls `ActiveHoursValidator.Validate(request.ActiveHours)`; on `!IsValid` → `BadRequest(error)` and the proxy is never called. `null`/whitespace passes as "clear" → `SetScheduleAsync(..., "[]")`.
- **Trigger** is `[EnableRateLimiting("test-alert")]` (see §6). Doc-comment must say *flush-and-deliver-now*, not "queued."
- **503 mapping:** `SummaryBackendUnavailableException` → `503 { error = "Quest summary service unavailable." }`, mapped by a small global filter registered in `Program.cs` next to `FeatureDisabledExceptionFilter`. No upstream URL/secret/exception text leaks.

### 4.4 `ISummaryCapabilityService` — config-derived, cached

**New files:** `Core\...\Core.Abstractions\Services\ISummaryCapabilityService.cs`, `Core\...\Core.Services\SummaryCapabilityService.cs`.

Reads `tracking.quest_summary_enabled` from `IPoracleApiProxy` (config proxy), caches the boolean (including `false`) under `summary_capability:quest` with a 5-min TTL, mirroring `SiteSettingService`. No write path → no explicit invalidation. Any fault → `false` (graceful degradation, like `pokemon-availability.service.ts` `catchError`).

```csharp
public async Task<bool> IsQuestSummaryEnabledAsync()
{
    if (this._cache.TryGetValue(CacheKey, out bool cached)) return cached;
    var enabled = await this.ProbeConfigAsync();      // tracking.quest_summary_enabled; false on any fault
    this._cache.Set(CacheKey, enabled, CacheTtl);
    return enabled;
}
```

The cache key is **server-wide** (not user-scoped) — capability is a deployment property and the cached value is a bare boolean (no PII).

### 4.5 Shared `ActiveHoursValidator`

Extract `ValidateActiveHours` + `TryGetIntValue` (`ProfileController.cs:182-262`) **verbatim** into a public static `ActiveHoursValidator` (`Core\...\Core.Models\ActiveHoursValidator.cs`). Rules: null/whitespace = valid ("clear"); JSON array; ≤ 28 entries; each entry an object with `day` 1-7, `hours` 0-23, `mins` 0-59; both Number and String JSON kinds accepted.

- Re-point `ProfileController.cs:46,77` and the new `SummaryScheduleController` to `ActiveHoursValidator.Validate(...)`.
- Re-point `ActiveHoursValidationTests.cs` to the new type (its 25+ cases then cover the summary body unchanged).
- Pure method move (~80 LOC), no interface, no DI registration — a static method matches the existing call sites.

### 4.6 Models

**New file:** `Core\...\Core.Models\SummarySchedule.cs`

```csharp
public class SummarySchedule
{
    public string AlertType  { get; set; } = "quest";
    public string ActiveHours { get; set; } = "[]"; // raw JSON array literal; "[]" when cleared
}
```

The controller maps the proxy's `JsonElement` (`{ id, alert_type, active_hours }`) → `SummarySchedule` by reading `alert_type` and serializing the `active_hours` element to raw text. **Do not project the upstream `id`** (it's the user id — never echo it).

PUT DTO: `public class SummaryScheduleRequest { public string? ActiveHours { get; set; } }` (accepts the SPA's `JSON.stringify(entries)`).

### 4.7 DI registration

In `ServiceCollectionExtensions.cs`:
- After line 123: `services.AddHttpClient<IPoracleSummaryProxy, PoracleSummaryProxy>();`
- Near 82-89: `services.AddScoped<ISummaryCapabilityService, SummaryCapabilityService>();`
- `AddMemoryCache()` already present (line 31).
- Registration is **unconditional** — PoracleNG endpoints always exist when `Poracle:ApiAddress` is set (a hard app-wide dependency). Capability degrades at runtime via the probe, not via conditional DI (this differs from Golbat, whose entire API is optional).

---

## 5. Frontend design

Angular 21 standalone + signals. The management surface is a dialog launched from the Quests page toolbar.

### 5.1 `SummaryScheduleService` (`core/services/summary-schedule.service.ts`)

`@Injectable({ providedIn: 'root' })` singleton (so the capability signal + `loaded` guard are app-wide). Mirrors `ProfileService` for CRUD and `PokemonAvailabilityService` for the cached capability signal.

```ts
export interface SummarySchedule {
  alertType: string;            // 'quest' only today
  activeHours: ActiveHourEntry[];
}

@Injectable({ providedIn: 'root' })
export class SummaryScheduleService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  readonly enabled = signal(false);          // false => hide panel + annotate hint
  private get base() { return `${this.config.apiHost}/api/summary-schedules`; }

  loadCapability(): Observable<boolean>;       // GET /capability once; 5-min refresh; catchError keeps prior
  getSchedules(): Observable<SummarySchedule[]>;            // GET /api/summary-schedules
  getSchedule(alertType: string): Observable<SummarySchedule | null>; // GET /{alertType}; 404 -> null
  setSchedule(alertType: string, hours: ActiveHourEntry[] | null): Observable<void>; // PUT {activeHours}
  deleteSchedule(alertType: string): Observable<void>;     // DELETE /{alertType}
  trigger(alertType: string): Observable<void>;            // POST /{alertType}/trigger
}
```

- **No userId in any URL** — the backend reads the JWT.
- `getSchedule` maps a 404 → `null` (a missing schedule is normal, not an error).
- `setSchedule` stringifies; `null`/empty → `"[]"` (clear without deleting the row).
- `activeHours` is sent/parsed via `parseActiveHours` from `active-hours.models.ts` (handles PoracleNG string-typed `hours`/`mins`). **There is no `active-hours.utils.ts`.**

### 5.2 Component tree

```
QuestListComponent (existing, modified)
│  + inject SummaryScheduleService; call loadCapability() in ngOnInit
│  + toolbar mat-menu item "Quest summary delivery" — @if (summaryService.enabled())
│  + openSummaryDialog() -> dialog.open(SummaryScheduleDialogComponent, { width:'560px', maxHeight:'90vh' })
│
└── SummaryScheduleDialogComponent  (NEW — modules/quests/summary-schedule-dialog/)
       standalone, OnPush, signals
       │  schedule    = signal<SummarySchedule|null>(null)   // loaded lazily on open
       │  entries     = computed(() => schedule()?.activeHours ?? [])
       │  hasSchedule = computed(() => entries().length > 0)
       │  userLat/userLon = signal(0)  // from LocationService.getLocation()
       │  loading / saving / triggering = signal(false)
       │
       ├── <app-location-warning [hasActiveHours]="hasSchedule()"
       │       [latitude]="userLat()" [longitude]="userLon()" />     (REUSED)
       ├── read-only schedule chips via groupActiveHours + formatTime12h  (REUSED helpers)
       ├── "Edit schedule"  -> ActiveHoursEditorDialogComponent (REUSED, nested)
       ├── "Send summary now" -> service.trigger('quest')  (cooldown-guarded; disabled if !hasSchedule)
       └── "Clear / Delete" -> confirm -> service.deleteSchedule('quest')
```

**Nested editor reuse:** open `ActiveHoursEditorDialogComponent` with `{ activeHours: entries(), profileName: instant('QUESTS.SUMMARY_SCHEDULE_ALERT_LABEL') }` (`profileName` is **mandatory**). `afterClosed()` → `ActiveHourEntry[] | undefined`: array → `service.setSchedule('quest', result)`; `undefined` → no-op (cancel).

**Why a dialog, not a routed panel:** one schedule, three actions, per-user, inherits the `quests` route guard for free, and reuses the editor with zero routing plumbing.

### 5.3 Capability handling (203 vs 503 — corrected)

The SPA reads `enabled()` (a signal populated from the 200-body boolean), never an HTTP status.

- `disable_quests` off → the whole `/quests` route is unreachable (existing guard + 403 interceptor). No new work.
- Quest summary not enabled → `enabled() === false` → hide the toolbar menu item; if the dialog is somehow opened, render a disabled empty state (`SUMMARY_SCHEDULE` no-schedule + disabled hint) rather than crashing on a 503.
- A transient data-endpoint 503 → retry banner only, never "feature off."

### 5.4 Pairing with the #295 clean-bit toggle (hint text only)

The clean-bit-4 toggle (`quest-add-dialog.component.html:115`, `quest-edit-dialog.component.html:88`) is **out of scope to change**. The only change:

- When `enabled()` is **true** but no schedule is configured → existing `QUESTS.SUMMARY_HINT` stays, optionally with a CTA opening the summary dialog.
- When `enabled()` is **false** → append `QUESTS.SUMMARY_DISABLED_HINT` ("Summary scheduling isn't available on this server.") next to the toggle and hide the toolbar entry. Do **not** disable the checkbox or touch `isSummary(clean)`.

### 5.5 Routing

No new route. The dialog rides inside the existing gated `quests` route (`app.routes.ts:47`). Capability is a runtime signal, not a route gate — no `disable_summary` key.

### 5.6 i18n

New keys added to all **11** locales under `ClientApp/src/assets/i18n/`. Keep the set minimal; reuse existing `COMMON.*` and `PROFILES.ACTIVE_HOURS_*` where the reused editor already covers them.

```jsonc
"QUESTS": {
  // ...existing...
  "SUMMARY_SCHEDULE": "Quest summary delivery",
  "SUMMARY_SCHEDULE_ALERT_LABEL": "Quest summary",
  "SUMMARY_SCHEDULE_EMPTY": "No summary schedule set. Quests are delivered individually.",
  "SUMMARY_SCHEDULE_EDIT": "Edit schedule",
  "SUMMARY_SCHEDULE_CLEAR": "Remove schedule",
  "SUMMARY_SCHEDULE_SEND_NOW": "Send summary now",
  "SUMMARY_SCHEDULE_SAVED": "Summary schedule saved",
  "SUMMARY_SCHEDULE_CLEARED": "Summary schedule removed",
  "SUMMARY_SCHEDULE_SENT": "Summary queued for delivery",
  "SUMMARY_SCHEDULE_FAILED": "Couldn't update the summary schedule",
  "SUMMARY_SCHEDULE_UNAVAILABLE": "Summary delivery is temporarily unavailable. Please try again later.",
  "SUMMARY_DISABLED_HINT": "Summary scheduling isn't available on this server."
}
```

(Only `en.json` carries English values; the other 10 get the same keys translated.)

---

## 6. Security analysis

PoracleNG keys `summary_schedules` per-user with no `profile_no`. The proxy forwards `{id}` straight into the upstream path, so **any id the controller forwards is fully read/write/delete/trigger-able**. The controller is the only authorization boundary.

| # | Control | Requirement |
|---|---|---|
| 1 | **JWT-derived id, never request-derived (MUST)** | Controller derives `BaseApiController` and uses `this.UserId` (`BaseApiController.cs:13`, throws when absent) as the only source of `{id}`. No `{userId}`/`{id}` route segment; no id field in the body. A forwarded request-supplied id is a complete IDOR over read/write/delete/trigger. `Encode(this.UserId)` (`Uri.EscapeDataString`) on the path segment guards against injection from exotic ids. Impersonation tokens already set `this.UserId` to the impersonated user — correct, contained. |
| 2 | **Rate-limit the trigger (MUST)** | Trigger is flush-and-deliver-now (real DM). Apply `[EnableRateLimiting("test-alert")]` (5/60s, `user:{userId}`→IP partition; `TestAlertController.cs:9`, `Program.cs:379-385`). Also apply a write policy (`auth-read` 120/60s or stricter) to PUT/DELETE — each schedules a 500 ms debounced reload; a write loop keeps the reload timer perpetually armed. Reuse named policies (never a global limiter — the cascading-failure footgun). |
| 3 | **Validate `active_hours` before proxying (MUST)** | PUT validates via the shared `ActiveHoursValidator` (≤28 entries caps the array under the `varchar(4096)` column; day/hours/mins ranges; number+string kinds). Invalid → `BadRequest`, never forwarded. Validate `{alertType}` against the hardcoded quest-only allow-set on GET-one/PUT/DELETE/trigger. Do not hand-roll a second validator. |
| 4 | **`disable_quests` gate on every action (MUST)** | `[RequireFeatureEnabled(DisableFeatureKeys.Quests)]` on the controller class (mirror `QuestController.cs:10`) — 403 with `{error, disableKey}` before model binding. The Angular guard is cosmetic; the filter is the real boundary (the #236 lesson). Admins are intentionally not exempt. |
| 5 | **No internal leakage on errors (MUST)** | Proxy branches on 503 before `EnsureSuccessStatusCode()` and throws a typed exception (no raw `HttpRequestException` text to the client). Controller returns a generic `503 {"error":"Quest summary service unavailable"}` — no upstream URL, no `X-Poracle-Secret`, no `ex.Message`/`.StackTrace`, no verbatim upstream envelope. Map 404→`NotFound()`, 400→sanitized `BadRequest`, everything else→generic 502/500. Log detail server-side via `[LoggerMessage]` (mirror `TestAlertController`). |
| 6 | **Secondary hardening** | `X-Poracle-Secret` added only when non-empty; never logged. `0,0` location warning is UX, not a security control. JWT bearer (not cookies) → no CSRF token; CORS already origin-whitelisted in non-dev (`Program.cs:236-260`). Idempotent DELETE returns uniformly (no existence probe) — moot since id is JWT-derived, but keep responses uniform. |

---

## 7. Performance analysis

| Path | Cost | Mitigation |
|---|---|---|
| Quests page load | **+0 round-trips** in steady state | Capability is fetched **once** per app session (`loaded` guard, `pokemon-availability.service.ts` pattern) and cached server-side 5 min via `IMemoryCache` on the config read. Never probe a 503-capable endpoint on every navigation. Optionally fold `questSummaryEnabled` into `auth/me`/`UserInfo` for literally zero extra calls. |
| Panel open | exactly **1** proxy call | `GET /api/summaries/{id}` returns the full `{schedules:[...]}` in one shot. Do not loop `GET /{alertType}`. **Lazy-load on dialog open**, not on `/quests` render. Do not regress the dashboard's "8 counts → 1 call" ethos. |
| Schedule save | **1** PUT → **1** coalesced 500 ms reload | The reused editor returns the **whole** `ActiveHourEntry[]` from `save()`; PUT once on dialog close, not per-rule edit (which would arm N reloads). |
| Trigger | **1** POST, cooldown-guarded | Synchronous flush-and-deliver delivers a real DM. Add a client cooldown + in-flight dedup like `TestAlertService` (15s) so a double-click can't double-deliver / double-flush. Trigger does not reload, so the cooldown is purely a duplicate-delivery guard. |
| Payloads | sub-KB | `active_hours` ≤28 entries, column `varchar(4096)`. Pass through as raw JSON (no `JsonSerializerOptions` allocation, no naming policy). No compression/paging. |
| Validation | in-process CPU | Reuse the shared validator before proxying — rejects malformed input without a wasted round-trip to PoracleNG. |

**Service lifetime:** `SummaryScheduleService` is `providedIn: 'root'` so the capability signal and `loaded` guard are shared app-wide (a per-component service would re-fetch on every Quests visit).

---

## 8. Test matrix

Conventions: backend xUnit `<Sut>Tests` under `Tests/Pgan.PoracleWebNet.Tests/{Services,Controllers,Validation}/`, in-class `MockHttpMessageHandler` + in-memory `IConfiguration` for proxies, `ControllerTestBase.SetupUser(...)` (default `userId="123456789"`) for controllers. Frontend Jest specs co-located, `HttpTestingController` for services, `fixture.componentRef.setInput(...)` for components.

### 8.1 Backend

**`PoracleSummaryProxyTests.cs`** (mirror `PoracleHumanProxyTests`)
- `GetSchedulesAsyncUnwrapsSchedulesArrayOn200`, `GetSchedulesAsyncReturnsEmptyWhenSchedulesArrayEmpty`
- `GetScheduleAsyncUnwrapsScheduleObjectOn200`, `GetScheduleAsyncReturnsNullOn404`
- `GetSchedulesAsyncCallsCorrectUrl`, `GetScheduleAsyncCallsCorrectUrlWithAlertType`
- `SetScheduleAsyncSendsPostWithActiveHoursBody` (raw `active_hours` passed through, **no snake_case policy assertion**), `SetScheduleAsyncCallsCorrectUrl`, `SetScheduleAsyncThrowsOnNon2xx` (400/500)
- `DeleteScheduleAsyncCallsCorrectUrl`, `DeleteScheduleAsyncSucceedsOnIdempotent200`
- `TriggerAsyncCallsCorrectUrl`, `TriggerAsyncSucceedsOn200` (always-200 flush)
- `SetScheduleAsyncThrowsBackendUnavailableOn503`, `GetScheduleAsyncThrowsBackendUnavailableOn503` (503 branched before `EnsureSuccessStatusCode`, distinct exception)
- `AllRequestsIncludePoracleSecretHeader`, `AllRequestsOmitSecretHeaderWhenEmpty`
- `UserIdIsUriEscapedInPath` (`a/b` → encoded segment)

**`SummaryScheduleControllerTests.cs`** (derives `ControllerTestBase`)
- `GetSchedulesUsesJwtUserIdNotPath`, `GetScheduleUsesJwtUserId`, `SetScheduleUsesJwtUserId`, `DeleteScheduleUsesJwtUserId`, `TriggerUsesJwtUserId` (authz core — id is always `this.UserId`)
- `SetScheduleInvalidActiveHoursReturnsBadRequest` (proxy `Verify(..., Times.Never)`)
- `SetScheduleValidActiveHoursCallsProxy`, `SetScheduleEmptyArrayClearsScheduleReturnsNoContent`
- `[Theory] SummaryEndpointsRejectNonQuestAlertType` — `("raid")`, `("invalid")`, `("")` → `BadRequest` before proxy
- `[Theory] SummaryEndpointsAcceptQuestAlertType` — `("quest")` reaches proxy
- `SummaryBackendUnavailableMappedTo503` (transient signal, distinct from feature gate, no `ex.Message` leak)
- `SummaryEndpointsThrowFeatureDisabledExceptionWhenQuestsDisabled` (feature gate throws → 403; proxy never called; #236 case)
- `SummaryEndpointsAdminAlsoBlockedByDisabledQuests` (`isAdmin:true` not exempt)
- `GetCapabilityReturnsEnabledFlagInOkBody` (`{ enabled: bool }` in a 200, not a 503)
- `TriggerActionCarriesTestAlertRateLimitAttribute` (reflection assertion)

**`ActiveHoursValidationTests.cs`** (re-pointed to the extracted `ActiveHoursValidator`)
- The existing 25+ cases (`ValidSingleEntry`, `InvalidDay8`, `InvalidHours25`, `InvalidMins60`, `InvalidTooManyEntries`, `Valid28Entries`, string-typed hours/mins, non-array, empty-as-clear) now cover the summary body unchanged.
- `SummaryScheduleControllerDelegatesToActiveHoursValidator` (smoke — no forked validation logic).

### 8.2 Frontend

**`summary-schedule.service.spec.ts`** (mirror `pokemon-availability.service.spec.ts`)
- `should be created`
- `getSchedules GETs /api/summary-schedules`, `getSchedule GETs /{alertType}`
- `setSchedule PUTs serialized [{day,hours,mins}] to /{alertType}`
- `deleteSchedule DELETEs /{alertType}`, `trigger POSTs /{alertType}/trigger`
- `capability exposes enabled=true from a 200 body`, `capability false when API reports off`
- `capability defaults to false and does not throw on 503` (`catchError`, signal stays false)
- `capability load is idempotent` (two `loadCapability()` → one request)

**`summary-schedule-dialog.component.spec.ts`**
- `should create`
- `hides schedule UI when capability disabled`, `shows schedule UI when enabled`
- `opens ActiveHoursEditorDialogComponent seeded with current schedule and a profileName label` (REUSE, no bespoke editor)
- `persists editor result by stringifying [{day,hours,mins}] and calling setSchedule`
- `does not call setSchedule when editor cancelled` (`afterClosed` → `undefined`)
- `calls trigger on "Send summary now"`, `snackbar on success`, `error snackbar on failure`, trigger cooldown dedup
- `renders LocationWarning inputs when 0,0 and a schedule exists`, `no warning when coords set`

**`quest-list.component.spec.ts`**
- `exposes the Quest summary delivery action only when capability enabled`
- `opens SummaryScheduleDialogComponent from the toolbar action`

**`quest-add-dialog.component.spec.ts` / `quest-edit-dialog.component.spec.ts`**
- `appends SUMMARY_DISABLED_HINT when capability off; clean SUMMARY bit toggle behavior unchanged` (annotation only)

**i18n parity** — the existing key-parity check must confirm the new `QUESTS.SUMMARY_SCHEDULE_*` / `SUMMARY_DISABLED_HINT` keys exist in all 11 locale files.

---

## 9. Simplification decisions (and rationale)

| Question | Decision | Rationale |
|---|---|---|
| Editor: reuse or new? | **100% reuse `ActiveHoursEditorDialogComponent`, 0 new editor LOC** | Fully profile-decoupled (`profileName` is only a title label). `save()` returns exactly the `ActiveHourEntry[]` the PUT needs. Do not fork to rename its `PROFILES.*` i18n keys (cosmetic). |
| Validator: reuse, duplicate, or DI service? | **Extract `ValidateActiveHours` + `TryGetIntValue` to a static `ActiveHoursValidator`** | Pure method move (~80 LOC), already test-covered. Duplicating risks drift between profile and summary validation. A DI-injected `IActiveHoursValidator` is gold-plating — no state, no polymorphism. |
| Capability: dedicated endpoint, fold into `auth/me`, or cached probe? | **One mechanism: a config-derived boolean.** Surface it as `GET /capability` (200 body) OR fold into `auth/me`/`UserInfo`. Either way, cache the **config read** 5 min. | The issue's "probe-the-503" premise is invalid (§2.5). Delete: any 503→capability inference. The config flag is the only source of truth. Folding into `auth/me` costs 0 extra round-trips; a dedicated 200-boolean endpoint matches the Golbat precedent. |
| Proxy: dedicated or extend `IPoracleHumanProxy`? | **Dedicated `IPoracleSummaryProxy`** | Different endpoint family (`/api/summaries/*`), per-user not per-profile, and `AddHttpClient<T,T>` is the established unit. But **cut** the phantom `SnakeCaseLower` policy (pass raw JSON) and copy `SendAsync`/`Encode` verbatim. |
| 503 → typed exception? | **Keep one `SummaryBackendUnavailableException`** for the transient-503 banner. **Do not** build a `SummaryFeatureDisabledException` whose only consumer is the invalid 503→capability inference. | The 503 is an outage signal; the feature flag is read elsewhere. |
| New route / nav item / feature key? | **None.** | Inherits `disable_quests` via the `/quests` route; entry point is a toolbar `mat-menu` item (no nav clutter, per recorded preference). Adding a `disable_summary` key would touch all 5 feature-gating layers for zero benefit. |
| Model shape | **Minimal `{ AlertType, ActiveHours }`** | Table is per-`(id, alert_type)` with no `profile_no`; no per-profile fields. |
| `alertType` typing | **Hardcoded quest-only `HashSet` allow-set** | Only one value is wired upstream; an enum/registry is premature. Mirrors `TestAlertController`. |

**Net effect:** removed from the issue-as-written — a capability proxy probe, a capability cache keyed off a 503, a second typed exception, a snake_case serializer, and any forked editor. What remains: one dedicated proxy (raw-JSON pass-through), one controller (JWT-id only, reused validator), one minimal model, one frontend service, one reused dialog, one reused warning component, a config-derived `questSummaryEnabled` boolean, and i18n.

---

## 10. Phased delivery plan

Mirroring how #292 was split (PR1 preservation / PR2 toggles), this lands in independently-reviewable, independently-shippable PRs. Earlier PRs carry no UI risk; the UI PR is last and gated behind a capability flag that defaults to hidden.

### PR1 — Shared validator extraction (no behavior change)
- Extract `ProfileController.ValidateActiveHours` + `TryGetIntValue` → `ActiveHoursValidator` (`Core.Models`).
- Re-point `ProfileController.cs:46,77` and `ActiveHoursValidationTests.cs` to the new type.
- **Why first / standalone:** pure refactor, zero new endpoints, de-risks the rest. Reviewable in isolation; if it regresses profile active-hours, it's caught before any summary code exists.
- **Tests:** existing `ActiveHoursValidationTests` (re-pointed) must stay green.

### PR2 — Backend proxy + capability (no UI)
- `IPoracleSummaryProxy` / `PoracleSummaryProxy` (raw-JSON pass-through, `SendAsync`/`Encode` copy, 503→`SummaryBackendUnavailableException`).
- `SummarySchedule` model + `SummaryBackendUnavailableException`.
- `ISummaryCapabilityService` / `SummaryCapabilityService` (config-derived boolean, 5-min cache).
- DI registrations; global 503 exception filter.
- **Why second / standalone:** the whole backend can ship and be exercised by tests/Swagger before any SPA wiring. No UI references it yet, so no user-visible change.
- **Tests:** `PoracleSummaryProxyTests`, `SummaryCapabilityServiceTests`.

### PR3 — Controller + endpoints
- `SummaryScheduleController` (`[RequireFeatureEnabled(DisableFeatureKeys.Quests)]`, JWT-id only, quest-only allow-set, shared validator, `test-alert` rate limit on trigger, PUT-upsert).
- `GET /capability` (or the `auth/me` fold, whichever is chosen — decide in PR2's review).
- **Why split from PR2:** keeps the IDOR/rate-limit/feature-gate security surface in one focused, security-reviewable PR with the full controller test matrix.
- **Tests:** `SummaryScheduleControllerTests` (full §8.1 controller set).

### PR4 — Frontend service + capability gating (no panel yet)
- `SummaryScheduleService` (`providedIn:'root'`, capability signal, CRUD, trigger cooldown).
- Wire `loadCapability()` into `QuestListComponent.ngOnInit`.
- Append `QUESTS.SUMMARY_DISABLED_HINT` annotation to the existing clean-bit hint when capability off (hint text only).
- i18n keys (the annotation + service-level strings) across all 11 locales.
- **Why before the dialog:** the capability plumbing and hint annotation are low-risk and let the team verify the "hidden when off" behavior independently of the editing UI.
- **Tests:** `summary-schedule.service.spec.ts`, quest-dialog hint specs.

### PR5 — Summary schedule dialog (the user-facing panel)
- `SummaryScheduleDialogComponent` (reuses `ActiveHoursEditorDialogComponent` + `LocationWarningComponent`).
- Toolbar `mat-menu` entry point in `quest-list.component.html`, gated on `enabled()`.
- "Send summary now" / "Clear / Delete" actions; remaining i18n keys across 11 locales.
- **Why last:** the visible feature lands only once every layer beneath it is merged and tested. The capability gate (PR4) means a half-finished or disabled deployment never exposes a broken panel.
- **Tests:** `summary-schedule-dialog.component.spec.ts`, `quest-list.component.spec.ts`; i18n parity.

**Sequencing guarantee:** each PR compiles and ships on its own. PR1-3 are invisible to users; PR4 only adds a (possibly false) capability signal + a hint; PR5 is the first PR that surfaces a clickable feature, and it is gated. A rollback of any single PR leaves the system in a coherent state.
