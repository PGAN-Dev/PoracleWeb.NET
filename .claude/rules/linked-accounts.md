---
paths:
  - "**/*AccountLink*"
  - "**/*account-link*"
  - "**/*LinkedAccount*"
  - "**/*linked-account*"
  - "**/*LinkedAlarm*"
  - "**/*LinkedPair*"
  - "**/*PairDrift*"
  - "**/*pair-drift*"
  - "**/*PairedHuman*"
  - "**/*PairWarning*"
  - "**/*pair-warning*"
  - "**/*AlarmMove*"
  - "**/*alarm-move*"
  - "docs/features/linked-accounts.md"
---

## Linked Accounts (Discord and Telegram)

A person who uses the site from both platforms has two Poracle humans, `discord:user` and `telegram:user`.
`poracle_web.account_links` pairs them (one link per account, `default_destination` per pair); the user-facing
reference is `docs/features/linked-accounts.md`. **A link pairs `discord:user` with `telegram:user` and nothing
else**: never a destination type (webhook, channel), never another `:user` string. All four sign-in paths already
refuse destination rows, and linking refuses them with a code the page translates.

- **`ILinkedAccount` resolves live**, for the current `UserId`, cached one minute and dropped on link, unlink,
  default change and purge. A side whose live row's `type` doesn't match its column is treated as absent. With
  `disable_account_linking` on it answers the caller's own human alone, so every person acts as one account and
  link rows are kept. A paused side (admin disabled, stopped, not registered) is reported and **never
  re-enabled** by PoracleWeb.NET. **It fails open**: a failed `account_links` read (the `poracle_web` database
  down) or a row it can't read (`AccountLinkUnreadableException`, an unknown `default_destination`) answers
  `LinkedPair.Single`, logs a warning and is not cached, so an outage never stops an unlinked person's writes.
  A limit that must hold while a side is missing (the drawn-fence cap) counts `LinkedHumanIds`, every id the
  link names, not `ExistingHumanIds`.
- **`LinkedAlarmRouter`** is the only place alarm controllers learn which human(s) to read or write. Unlinked, it
  makes exactly today's PoracleNG calls and the alarm JSON is byte-identical (`destination` is omitted when
  null). Linked, a read concatenates both humans and stamps `destination`; a uid lookup finds the other side;
  bulk work groups per owner.
- **`AlarmMover` creates before it deletes**, so a move never loses an alert. A refused preflight or create
  changes nothing; a delete that fails twice answers 200 with `sourceRemains` and the list shows both copies,
  tagged. Quick-pick applied state follows the uid. The answer carries `X-Move-Status` (`moved` or
  `source_remains`), and the SPA reads it from the header because the body is the alarm as it now stands.
- **`PairedHumanWriter` carries every person-level write** (location, places, areas, profiles, language, quiet
  periods, alerts on/off, summary schedules): the signed-in human first, then the other. The first failing is
  today's error and the second isn't tried. The second failing still answers success, with the header
  **`X-Pair-Warning`** (`{"side":"telegram","reason":"...","code":"unreachable"}`, the side that did not take it). The warning
  never changes a body or a status (the Cleaning toggles carry it too, through `CleanToggleResult`), and both headers are CORS-exposed (`CorsPolicySetup.ExposedHeaders`; an
  unexposed header reads as absent and a half-done move looks clean). **`code` is what the SPA words**
  (`PairWarningCodes`: `unreachable`, `out_of_step`, `rename_refused`, and `refused`, which carries PoracleNG's
  own sentence as `reason`); `reason` is the English fallback and is never shown for a known code. The SPA's
  `pairWarningInterceptor` is the one reader: "Saved for Discord, but not for Telegram: {reason}. Review the
  difference on Linked accounts." once per response, through `ToastService.pairWarning`, whose action opens
  Linked accounts. **The page's own success toast lands a moment later and used to replace the warning
  unread**, so a `success`/`info` within the warning's 8 s carries it appended instead. The interceptor also
  calls `LinkedAccountService.refreshSoon()` after every successful non-GET outside `/api/account-link`,
  `/api/auth` and `/api/test-alert` (debounced, linked only), so the banner and the list never offer to undo
  what the person just did.
- **Drift is never resolved silently.** `IPairDriftService` compares location (5 decimal places), the active
  profile's areas (PoracleNG's `NormalizeAreaKey`, order ignored), current profile, profiles and saved places,
  and `GET /api/account-link/status` carries the items as `drift` (`null` when the read failed, which the SPA
  treats as "no banner" and never retries in a loop). Each item has a `fingerprint` over both sides' values;
  `POST /api/account-link/resolve { field, keep, expected }` re-reads, and otherwise applies the kept side to the
  other. Every refusal is a `DriftRefusalException`, which `PairDriftExceptionFilter` answers 409
  `{ error, code, side, name, count }`: `drift_changed` (the fingerprint no longer matches), `profile_holds_alarms`
  and `place_in_use` (the other app still has alarms on it, "Nothing was changed"), `profile_missing` (switching the
  active profile to one the other app lacks: switching never creates a profile, so the profiles line goes first,
  and the list puts it first), and `partially_applied`. The SPA words the code; `error` is English fallback.
  Items carry `details` outside the fingerprint (active-profile names and whether the other app has that number;
  place labels as typed), so a line names things without a rename staling an unrelated fingerprint. **The profile
  either side is on is compared without its areas and location**: the humans hold its live copy, which the location
  and areas lines compare, and comparing both reported one cause twice (with the sides on different profiles too);
  resolving profiles leaves that geography alone for the same reason. The profiles item's `details` carry each side's
  `current`, so the panel says when keeping one side's profiles deletes the profile the other app is on and so
  switches it, which changes that app's areas and location as well. **Two surfaces.** The banner (`PairDriftBannerComponent`, under the toolbar in
  `app.html`, hidden while impersonating and on Linked accounts) is one line and a Review link; it appears only with
  the first status read or at a navigation, never mid-page, and dismisses per fingerprint into `sessionStorage`
  inside try/catch. The list (`PairDriftPanelComponent`, on Linked accounts) always shows every difference,
  dismissed or not: a label per line, what differs by name, what each choice does to the other app (as the
  button's description), a confirm naming what a choice deletes (its button says "Delete Weekend from Telegram"), the outcome
  as a `ToastService.success` toast (a `role="status"` line at the panel's foot was below the fold on a phone), and
  focus to the next difference or the heading. Its heading claims a difference only above a list of them. Locations are named at the same reverse-geocode level on both sides
  (`sameLevelNames`) with the distance between them, never as coordinates; area names come from
  `/api/areas/available` in PoracleNG's spelling, asked only when an areas line exists and `disable_areas` is off.
  **Profile 0 is outside the pair**: PoracleNG never numbers a new profile 0, so the drift, resolve and
  register-missing code never compares, copies, creates, renames, switches to or deletes it (a current profile
  of 0 on either side skips that comparison), its alerts stay on the account that has it, and `status` names the
  side that has one the other lacks as `legacyProfileZero` for a one-line note on the page.
- **`/api/account-link` is in the error interceptor's silent list**, so `LinkedAccountService` callers word
  every outcome; the list words a resolve refusal inline (`role="alert"`, never a toast, which covered the heading
  on phones), beside the button pressed, or under the heading when that line has gone and the service refreshes status after any resolve, success or not.
- **Don't compare what drifts on its own.** Language, summary schedules and quiet periods are written to both
  but not compared (quiet periods live in PoracleNG's memory and vanish on restart).
