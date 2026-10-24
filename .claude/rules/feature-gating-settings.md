---
paths:
  - "**/*Feature*"
  - "**/*feature*"
  - "**/*SiteSetting*"
  - "**/*Setting*"
  - "**/*settings*"
  - "**/disabled-feature*"
  - "**/UpstreamFeatureFlag*"
---

## Settings Architecture
- Site settings are stored in typed columns in `poracle_web.site_settings` with categories (branding, features, alarms, admin, etc.) and typed values.
- Webhook delegation uses the `poracle_web.webhook_delegates` relational table with a composite unique constraint (user + webhook), replacing an old key-prefix pattern.
- Quick pick definitions use `poracle_web.quick_pick_definitions` with JSON columns for filter definitions. Applied state is tracked in `poracle_web.quick_pick_applied_states` per user/profile, replacing the old `quick_pick:`, `user_quick_pick:`, and `qp_applied:` key prefix patterns.

## Feature Gating (`disable_*` Site Settings)
The `disable_mons` / `disable_raids` / `disable_quests` / `disable_invasions` / `disable_lures` / `disable_nests` / `disable_gyms` / `disable_maxbattles` / `disable_fort_changes` site settings disable entire alarm types for everyone, including admins. Eggs share `disable_raids` (no separate `disable_eggs` exists; eggs share the raid UI in the SPA). See #236 for the original bug.

**A disabled alarm type disappears completely.** The gate is **class-level** on the ten alarm
controllers and on `SummaryScheduleController`, so every action answers 403 — reads and deletes
included — and the SPA hides the sidebar item, the dashboard card and the route.

This was briefly the other way round. #784 moved the gate onto the write actions so users could still
see and delete rules of a switched-off type; #792 reverted it. An operator disabling a type means it
should be gone, and dormant rules are harmless: they cannot fire, they are not deleted, and they come
back intact when the type is re-enabled. `DisabledAlarmTypeGatingTests` fails the build if the gate
moves back onto individual actions.

The corollary is that there is nothing read-only to build around it: no banner, no `writesDisabled`
computed, no per-control `@if`. If you find yourself adding one, the page it would live on is
unreachable.

**Any `disable_*` toggle must be enforced server-side.** A toggle wired only into the SPA (nav item, route guard) is decoration, not a gate: the endpoints stay reachable by direct API call, and client state — the `siteSettings` signal — is trivially tampered with. `disable_areas`, `disable_profiles` and `disable_location` all shipped that way and were only closed later. `FeatureGateCoverageTests` now fails the build if a key is added to `DisableFeatureKeys` without a controller enforcing it.

Non-alarm features follow the same rules, minus the tracking-type dictionary: add the constant, apply `[RequireFeatureEnabled(...)]` to the controller (or to individual actions, as `UserGeofenceController` does so its reads stay open), add a `disabledFeatureGuard` to the route in `app.routes.ts` **and** the `disableKey` to the nav item — the nav entry alone is not enough.

Deliberately **not** gated: `/api/auth/me` under `disable_profiles`, so the JWT profile resync keeps working and PoracleNG's active-hours scheduler can still move a user between profiles.

`disable_nominatim` **is** implemented (#420): it gates the two geocode actions on `LocationController`, so switching it off genuinely stops address search and reverse lookup, and the location dialog hides its address search rather than firing a request that would 403 and bounce the user to the dashboard. It is gated per-action rather than per-controller because the controller itself is already gated by `disable_location`. The key predates #845 (see "PoracleNG Geocode Proxy" below), which moved the calls themselves off Nominatim specifically onto whatever PoracleNG is configured for -- the toggle keeps its original name because renaming a site setting breaks every instance that has it set.

**The SPA must not send a request a `disable_*` switch will refuse**, even one whose caller swallows the error: the error interceptor toasts the 403 before any `catchError` runs. `LocationService` and `PlacesService` check `disable_location` (and `disable_nominatim` for the two geocode calls) themselves and answer empty, or fail without a request where the caller expects an error, so the dashboard, Areas & Places and the scope picker in every add dialog stay quiet. The Areas page hides its pin card and Places section under `disable_location`, and the dashboard's quick actions, tips and the user menu carry the same `disable_*` keys as the sidebar. See #915.

`disable_geomap` and `disable_geomap_select` were removed from the admin UI and from `SettingsMigrationService` (now only the defaults seeder) in the same change. They are legacy PoracleJS keys describing a map picker PoracleWeb does not have, so there was nothing to wire them to and inventing a meaning would have been worse than deleting them. Any rows left in `site_settings` are harmless -- nothing reads them. `disable_userlist` was never a toggle in this UI (`admin_disable_userlist` only ever existed as a legacy key).

**Poracle's own flags are a floor under these (#769).** `UpstreamFeatureFlagService` reads `disabledHooks` from `/api/config/poracleWeb`, maps them to `disable_*` keys via `PoracleDisabledHookMap`, and `FeatureGate` treats a type as off if **either** source disables it. Cached 5 min. It **fails open**: any fault, timeout or absent field yields an empty set, because a Poracle outage disabling every alarm type for everyone is worse than the problem being solved. `GET /api/settings/upstream-disabled` exposes the resolved keys so nav, route guards and the admin toggles agree with the API.

Two traps, both found on 5.1.0, both filed upstream as jfberry/PoracleNG#195 and both fixed in 5.2.1 (jfberry/PoracleNG#197), so both are gone from every supported server. `pokestop` was in `disabledHooks` while `DisablePokestop` had no consumer in the processor, so it maps to **nothing** — mapping it to lures/invasions/quests would disable three working types. It is gone from the array now; the empty mapping costs nothing and stays. `disable_fort_update` was enforced upstream but omitted from the array; `fort` is in it as of 5.2.1 and maps like any other hook.

There used to be a second `/api/config/values` read of `general.disable_fort_update` for servers too old to report `fort`, recognised by the absence of `availableLanguages`. It went when 5.2.1 became the minimum (see "Minimum PoracleNG Is 5.4.0"); do not bring it back for an unsupported server.

**Adding a new alarm type? Wire it through all four layers:**

1. **Constant** — add to `Core/Pgan.PoracleWebNet.Core.Models/DisableFeatureKeys.cs` (both the `const string` field and the `ByTrackingType` dictionary entry).
2. **Service-layer guard** — inject `IFeatureGate` and `await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.X)` at the top of `CreateAsync`, `BulkCreateAsync`, and `UpdateAsync`. Throws `FeatureDisabledException` when disabled. Closes service-to-service bypasses (quick-pick apply, profile duplicate/import, cleaning toggle).
3. **Controller filter** — `[RequireFeatureEnabled(DisableFeatureKeys.X)]` on the controller class. Resource filter that 403s before model binding via `IFeatureGate.IsEnabledAsync`. The global `FeatureDisabledExceptionFilter` (registered in `Program.cs`) also catches `FeatureDisabledException` from any service path and returns the same 403 — both paths share `FeatureDisabledResponse.Create(disableKey)` so the wire format can't drift.
4. **Frontend** — add a `disabledFeatureGuard('disable_X')` to the route in `app.routes.ts`, and add the matching `disableKey` to the nav-item definition in `app.ts`. The 403 interceptor automatically redirects to `/dashboard` with an `ERROR.FEATURE_DISABLED` toast when the response body has a `disableKey` field.

**Caching:** `SiteSettingService.GetByKeyAsync` is wrapped in `IMemoryCache` with a 5-min TTL and explicit invalidation on writes. Without this, gate checks would add ~10 MySQL roundtrips per dashboard load. There's a documented TOCTOU window (slow read overlapping a write can leak a stale value for up to the TTL) — acceptable for admin-rare toggles.

## Settings That Are Projections, Not Rows

`poracle_locale` is synthesized onto the settings response from Poracle's `general.locale`; the SPA uses it as the last display-language fallback. It is **not stored**, `SettingsController.Upsert` refuses to write it, and it is declared in `PROJECTED_KEYS` so it never reaches the admin page's "Other" catch-all as an editable box. A stored row would win over the projected value, so one accidental save would pin the language default permanently. Any future projection needs the same two halves — the write refusal is the guarantee, the declaration is cosmetics. See #780, and #560 for the same mistake with retired keys.

`poracle_alert_languages` is the second, added with the same two halves: `Upsert` refuses it and it is
in `PROJECTED_KEYS`. It carries Poracle's `availableLanguages` as CSV and governs the **alert** language
menu — which codes Poracle will accept for `humans.language`, answering 422 to anything else. Not the
display language, and nothing to do with `allowed_languages`, which is this site's own restriction on
the display menu. No row is served when Poracle restricts nothing, and absent, `null` and an empty list
all mean exactly that: a Poracle with nothing configured accepts any code, so it gets the full menu.

`hidden_areas` is a real row, but one another page owns: Admin → Areas (`PUT /api/admin/areas`) writes it
and then asks Poracle to reload the geofence feed, which is what makes a change take effect. The generic
`PUT /api/settings/hidden_areas` therefore refuses it, and the SPA lists it in `MANAGED_ELSEWHERE_KEYS` so
it stays out of "Other". The same two halves as a projection, for a different reason.

Every `PUT /api/settings/{key}` is also bounded by the `site_settings` columns -- `key` 100 characters,
`category` 50, `value_type` 20, and `value` 65,535 **bytes** of UTF-8 (TEXT) -- and answers 400 over any
of them instead of the 500 a `DbUpdateException` produced. `PUT /api/admin/areas` writes the same column and
`HiddenAreas.TryValidate` applies the same byte bound (`HiddenAreas.MaxValueBytes`, which
`SettingsController` shares): the 500-name and 200-character caps alone allow ~99 KB.
