---
paths:
  - "Applications/Pgan.PoracleWebNet.App/ClientApp/src/**"
  - "**/MasterData*"
  - "**/masterdata*"
---

## Failure Messages: One Message, And The Server's Reason Only When It Is One

The error interceptor toasts 0, 403, 404, 500, 502, 503 and 504 itself (`TOASTED_STATUSES`) for every URL
outside `SILENT_URL_PATTERNS`. **`reportedByInterceptor(err)`** says whether that already happened, and a caller
that would word the same failure again checks it: a single Add's inline refusal (`refusalOf`), a batch Add's
toast (`finishAdd`, and the Pokéstop events dialog's own loop) and anything new. The user reads one message.

**`serverReason(err)`** (`shared/utils/server-reason.ts`) returns the API's `{ error }` text for a **4xx only**.
A 5xx body is usually the server's English ("PoracleNG could not be reached. Try again shortly.", "An unexpected
error occurred."), and the caller's own key is translated. The exceptions are the few 5xx answers that carry a
next step, and those call sites **opt in by exact status**, never by range:

| Call site | Status | Why the sentence matters |
|---|---|---|
| Admin > Destinations, add | 502 | #482 half-write: "Check the webhook list before trying again." A retry on the generic message can create a second row |
| User geofences, approve / reject / delete | 502 | Koji refused; "the region may no longer exist" says what to reload |
| `TestAlertService` | 502, 501 | reads `err.error.error` itself (silent URL), PoracleNG's reason for not delivering |

`serverReason(err, { allow5xx: [502] })` is the form. A new endpoint that answers a 5xx with guidance must either
answer a fitting 4xx/409 instead or add its call site here, with a spec that posts the real body; a 500 from the
global handler and the 503 from `PoracleUnreachableExceptionFilter` stay translated. `failure-message-parity.spec.ts`
pins the shape. **`createFailureText(err, i18n, fallbackKey)`** is the add and edit dialogs' wording: the server's
reason, then the first message of an ASP.NET validation 400 (`{ errors: { Field: [...] } }`, which names the
field), then "Too many changes at once" for a 429, then the fallback key. All ten edit dialogs use it with
`ALARM.FAILED_UPDATE`.

## Localized Game Data

Pokemon names, types, form names and evolution chains come from PoracleNG, which translates them from its own i18n bundle: `GET /api/masterdata/monsters?locale={code}`, proxied through `MasterDataController.GetMonsters` and consumed by the SPA's `MasterDataService` in the same `forkJoin` as items and moves. The locale is the **display** language, so switching it re-fetches and re-emits on `ready$`; an open species picker updates in place. See #771.

- **Fallback, not failure.** An unreachable Poracle, or one whose answer carries no usable names, falls back to the cached English WatWowMap masterfile (`IMasterDataService.GetMonsterDataAsync`). Moves and items have no translated equivalent upstream and stay English.
- **Type names are identity, not display text.** `IconService` keys uicons on the English name and the filter chips track by it, so `applyMonsters` resolves the English name from the stable type id (`shared/utils/pokemon-types.ts`) and keeps the translated string beside it as a label, read via `getTypeLabel()`. Substituting the localized name blanks every type icon.
- **Untranslated keys are ignored.** If Poracle's game-data locale download failed it returns the key itself (`poke_25`); those are skipped so the English name survives rather than a raw key reaching the UI.
- The lone-base-form drop rule matches `/^normal/i` because form names are now translated ("Normale" in Italian). Do **not** simplify it to "species with exactly one form": 856 species qualify and two of them, Koraidon and Miraidon, are not base forms.

## Angular Patterns
- All components are **standalone** (no NgModules).
- Uses `inject()` function instead of constructor injection.
- Uses Angular signals for reactive state where applicable.
- Lazy-loaded routes in `app.routes.ts`.
- Services in `core/services/` use `HttpClient` to call the .NET API (including `ScannerService` for gym search, `TestAlertService` for test notifications).
- `MasterDataService` fetches monster data per display language and refetches on a language change; it no longer fetches the masterfile from GitHub in the browser. See "Localized Game Data".
- `TestAlertService` manages per-UID cooldown tracking (15s Map-based TTL) and in-flight request deduplication to prevent duplicate API calls.
- `GymPickerComponent` is a shared autocomplete component used in gym/raid/egg dialogs for gym selection with photo thumbnails and area names.
- `ActiveHoursEditorDialogComponent` is a shared dialog for editing profile schedule rules with day/time pickers and a weekly preview grid.
- `ActiveHoursChipComponent` renders compact amber schedule pills summarizing active hours on profile cards.
- `LocationWarningComponent` displays an inline warning when a profile has active hours but missing coordinates.
- **`unsavedChangesGuard`** (`core/guards/unsaved-changes.guard.ts`) asks before an in-app navigation drops staged edits on Admin > Areas and Admin > Server settings (pages implement `HasUnsavedChanges`; a reload or tab close is the page's own `beforeunload`). It **steps aside for an ending session**: with no access token, or a navigation to `/login`, it returns true without asking, because `endSession` and `logout()` drop the token before they navigate and "Stay" would leave the user on a page whose every request 401s. Any new session-ending path must clear the token first, or navigate to `/login`.
- **`PageTitleStrategy`** (`core/services/page-title.strategy.ts`, provided in `app.config.ts`) builds "Pokémon · {custom_title}". A route's `title` is an **i18n key**, not text, so it follows the display language; admin pages carry `data: { adminPage: true }` and read "Admin: Areas" so they aren't confused with the user's page. A route with no title, or a key with no translation, shows the site title alone.
- `AlertDefaultsService` (`core/services/alert-defaults.service.ts`) persists the user's preferred default delivery scope for **new** alarms -- mode (`areas`/`distance`) and default radius (km, clamped 0.1-100) -- to `localStorage` (`poracle-default-alert-mode` / `poracle-default-alert-distance-km`), mirroring the theme/accent/language pattern. All nine add-alarm dialogs and the quick-pick apply dialog seed their `distanceMode`/`distanceKm` form controls from it; the `AlertDefaultsDialogComponent` (user menu -> Alert Defaults) edits it. Client-side only -- no backend/API change; existing alarms are unaffected.

## UI Patterns
- **Design direction** (`design-foundations.spec.ts` pins it): colour is data or nothing (team edge on gym and raid cards, tier on egg cards, primary type on single-species Pokémon cards, state colours; no decorative tints or coloured edges); sentence case and no ALL-CAPS eyebrow labels; no pokéball watermarks or background shapes; motion only in answer to the user (dialogs, a removed item, a banner the user opened), no staggered entrances or hover lifts; body text in Atkinson Hyperlegible Next, titles in Plus Jakarta Sans, Roboto behind both, tabular figures everywhere.
- **Page header**: one global `.page-header` in `styles.scss` (pages don't restyle it): `h1`, one `.page-description` line (hidden under 600px), and `.header-actions` ending in the page's one primary action, a filled, labelled `mat-flat-button.page-primary-action` ("New alarm" on the ten alarm lists, "New profile", "New quick pick", "Draw geofence", "Enable all", the dashboard's "New alarm" menu). Secondary actions sit before it, stroked or as icons. No FABs.
- **Alarm lists**: the shared `app-alarm-card` (subject, status chips, neutral filter pills, scope chip, test/edit/delete, Details for PoracleNG's summary) and `app-alarm-list-toolbar` (search, count, sort, select mode) on all ten. Test button sends a sample notification via PoracleNG.
- **Bulk operations**: select mode on each list shows `app-bulk-action-bar`: "N selected", Select all / Deselect all as text buttons, tonal Change radius, and a filled red "Delete N"; pinned to the bottom of the screen on phones. The count is read to screen readers once it settles (`BULK_COUNT_SETTLE_MS`), not on every tick.
- **Skeleton loading**: pulsing skeleton placeholders while a list loads (a loading state, so it may move).
- **Sidenav**: permanent at widths above 1024px, an overlay with the toolbar's menu button at 1024px and below (`overlayNav` in `app.ts`, `OVERLAY_NAV_MAX_WIDTH`). Group labels are sentence case.
- **Accent themes**: Toolbar gradient, sidenav active link, and UI accent colors are customizable via user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.
- **Dark/light mode**: CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.
- **Onboarding wizard**: Shows on dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag. Steps are keyed by id and built from the `disable_*` settings: under `disable_location` / `disable_areas` the step is absent and its endpoint is not read, completion counts only the steps shown, and the alarm step links to the first enabled alarm type.
- **Active hours pills**: Amber schedule chips on profile cards showing compressed day-range + time summaries (e.g., "Mon-Fri 8:00 AM"). Uses `ActiveHoursChipComponent` with `compressDayRange` and `formatTime12h` utilities.
- **Location warning**: Inline red warning banner (`LocationWarningComponent`) displayed on profile cards when active hours are configured but the profile's coordinates are 0,0 (which defaults to UTC in PoracleNG's scheduler).
- **Admin geofence submissions**: a review queue first, then a table. Each `pending_review` submission is a wide triage row (map on the left; owner, points, size, region, the public area its centre falls inside, Reject…/Approve on the right; the buttons open `GeofenceApprovalDialogComponent` on that side via `data.mode`), oldest submission first. Every other fence is in one flat sortable table below (name, status, owner, region, points, created, submitted), filtered All/Active/Approved/Rejected; a rejected one keeps Review there, since the API accepts it. Maps are drawn for the queue only. Discord avatars next to owner and reviewer names; reviewer names from `reviewedByName`.
