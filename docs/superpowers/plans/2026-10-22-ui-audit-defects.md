# UI audit defects: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Fix the defects (not the design suggestions) found by the 2026-10-22 UI audit of PoracleWeb.NET.

**Architecture:** Angular 21 SPA fixes plus a few .NET API guards; no schema changes. Branch `fix/ui-audit-defects`, based on `docs/ui-audit-refresh` (PR #1013), which already fixed the translation/copy gaps.

**Tech stack:** Angular 21 + Material 3 (`mat.theme`), Jest; .NET 10, xUnit + Moq.

**Spec:** the audit reports, one per area, in `C:\Users\micha\AppData\Local\Temp\claude\E--PGAN-pogogit-PGAN-Poracle-Web\63149b56-6f76-4753-85d0-09b52537a398\scratchpad\uiaudit\`: `report-admin.md`, `report-alarms.md`, `report-flows.md`, `report-cross.md` (sections 1 Defects and 2 UX). Each defect id below (e.g. `admin D1`) refers to those files: read the entry for repro, evidence and suspected source. The reports were written against 5a10bc9e; PR #1013 changed i18n keys and some components since, so re-locate code before editing.

## Global constraints
- Every user-visible string goes through i18n with all 11 locales (en, da, de, es, fr, it, nl, pl, pt, pt-BR, sv) updated; keep `locale-parity.spec.ts` passing; use the existing plural helper (`translatePlural` pipe / service) for counts.
- English display text spells "Pokémon" and "Pokéstop". "PoracleWeb.NET" in full in any prose.
- Shape 1 (CLAUDE.md "Fixing Defects Without Causing Them"): every new guard/refusal gets a legitimate-case-still-passes test; find other callers before tightening a rule.
- Shape 2: a fix to one of the ten alarm list components / add dialogs / edit dialogs is checked against the other nine in the same task; say in the report why each sibling is fine or fix it.
- Watch a new test fail before the fix.
- C# files: check `git diff --numstat` after each .cs edit for whole-file line-ending churn and restore if needed.
- Commits: conventional, plain; no Claude-Session trailers, claude.ai links or Co-authored-by. CHANGELOG `[Unreleased]` entries (Fixed) for user-visible fixes, short.
- Checks before each task's commit: SPA `npm run lint`, `npm run prettier-check`, `npx jest` (full), and `npm run build` when templates changed; backend `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS= dotnet test --no-restore` when C# changed.
- Out of scope: the design suggestions (dashboard strip, map-first Areas, card anatomy, triage queue, etc.).

## Review focus
1. Destructive buttons once `color="warn"` works: every warn control must now render red, and no non-destructive control may turn red by accident.
2. A refused Add that stays open must still close on success, and a batch (multi-select) Add must keep its current behaviour.
3. Admin self-protection must not block an admin from deleting or blocking OTHER admins or users.
4. Route guards for unsaved changes must not fire after a successful save or on a clean page.
5. Radius validation must still allow the existing legal range and stored legacy values above 100 km on edit (the server cap is `AlarmDistance.MaxMetres`); only the dialog's own input range is enforced for new entries.

---

### Task 1: Theme, focus and motion
- cross D2: make `color="warn"` render (M3 `mat.theme` ignores `color`; add Material's colour-variant backwards compatibility for the theme, or an equivalent that maps `color="warn"` to the error role for buttons, icon buttons, FABs, chips, slide toggles, checkboxes). Verify every one of the 39 `color="warn"` sites is actually destructive or an error; change any that aren't.
- cross D6: visible `:focus-visible` outline on Material buttons, icon buttons, list items/nav links, tabs, menu items, chips; white/contrasting on the blue toolbar.
- cross §3.6: under `prefers-reduced-motion: reduce`, zero `animation-delay` too.
- admin D10: impersonation banner contrast (≥ 4.5:1; e.g. dark text on amber) and copy "Viewing as {{name}}. Changes you make are saved to {{name}}'s alerts." (11 locales).
- Tests: a spec or a style test proving the banner tokens/contrast if feasible; jest for anything touched. Visual check happens in the bed task.

### Task 2: Admin settings tells the truth
- admin D1: `enable_discord` / `enable_telegram` (and any other key the server treats as "absent = on": check `AuthController` and every reader of site settings for `!= "false"` style reads) display as ON when absent. Test both absent and explicit false/true.
- admin §2: settings search with no match shows "No settings match "{{query}}"".
- admin D6: unsaved-changes guard (`canDeactivate`) on Admin > Areas and Admin > Settings, using the app's confirm dialog; no prompt when clean or after save; `beforeunload` too.
- admin §2: rule repair card shows when the last scan ran ("Scanned 3 min ago").
- Tests for each.

### Task 3: Admin users and webhooks
- admin D4: server refuses `DELETE` of the admin's own account (mirror the #613 self-block guard and its message); SPA hides Block and Delete on the admin's own row. Tests: own id refused; another admin and a normal user still deletable.
- admin D5 / flows D12 pattern: Users block/unblock and delete failures show the server's `error` text when present.
- admin §2: delete-user dialog says what goes (alarms, geofences, delegate grants, quick picks) and its confirm button is destructive.
- admin D7: delegate dialog's Add enabled only for a picked autocomplete option; server refusal text shown.
- admin D8: Profile column `?? 1` (profile 0 shows 0).
- admin D9: webhook URL message matches the accepted pattern (http or https), or tighten to https only if the server requires it (check the server rule first; don't refuse existing http webhooks).
- admin §2: delete-webhook icon not `person_remove`.
- admin §2: non-admin hitting `/admin/*` gets a toast ("That page is for admins") instead of a silent bounce.

### Task 4: Refused Adds keep the user's work
- alarms D4: a single-row Add that the API refuses with 409 (takeover) keeps the dialog open, shows the message inline above the actions, and closes only on success. All ten add dialogs (Shape 2) and the quick-pick apply dialog where it applies.
- Name the clashing rule: have the API's 409 for a takeover carry the existing rule's uid (the dry run's `updated[].previous.uid` or `uid`), and the dialog render "Already covered by: <summary of that rule>" with an "Edit that one" action that closes the add dialog and opens that rule's edit dialog. Backend test for the uid in the body; SPA spec for the inline state.
- alarms D3: when a single Pokémon Add answers "already tracked" (unchanged) the message says the existing alarm was left as it is and points to Edit (e.g. "You already track this. Nothing was changed; edit that alarm to change its settings."). Check lure/other types' unchanged paths use the same wording.

### Task 5: Scope picker and bulk distance
- alarms D5: radius input min 0.1 / max 100 km enforced for the dialog: visible mat-error and Save disabled in every dialog using the scope picker (21 call sites + quick-pick apply). Editing a stored rule whose radius is outside the input range must still save other changes (Review focus 5): show the stored value and only validate when the user changes the radius.
- alarms D8: pass `profileAreas` to every `<app-scope-picker>` call site so "Anywhere in my areas" summarises the profile's areas (where-sheet already does).
- alarms D9: bulk distance dialog title/body/button reflect the selection ("Change radius for 3 alarms"); area names title-cased like the scope picker; vocabulary "radius" and "pin".

### Task 6: Alarm lists and dialogs, smaller defects
alarms D6 (Pokéstop events delivery tab order + clean description), D7 (fort empty "Message settings" heading hidden when nothing under it), D11 (dialog tab header full width), D13 (nest "All Pokémon" no "#0"), D14 (mega energy quest title names the species), D15 (Pokéstop event add: disable an event only when already tracked with the same scope; fix "Showcase Already tracked" spacing), D16 (test alert failure shows the server's `detail`/`error` like the 501 path), D17 (quest summary dialog hides Send now/Remove when no schedule; action row doesn't wrap), D18 (invasion grunt order puts Steel in type order and leaders/Giovanni last; Decoy grunt icon fallback; quest item icons fall back to a generic icon on 404), D19 (verify card-click selection in select mode; fix or correct Help), D10 (Cleaning page shows a partial state when only some rules of a type are clean: extend `/api/cleaning/status` additively with a count or partial flag, keep `AllClean`).

### Task 7: Location, pin and places
flows D2 (stale address after search then map click), D3 (empty latitude/longitude: `Number.isFinite`, mat-error, Save disabled), D6 (place delete confirm says deletion is refused while alarms point at it and lists them; on 409 show the server's rule list), D7 (place names refuse control/bidi/line-separator characters server-side with the same rule as `ProfileNameRules`, and client-side), D8 (place prompt: trim-required, maxlength 64 with counter, duplicate hint, server reason shown), U2 (Clear pin asks for confirmation naming how many alarms measure from the pin; de "Entfernen" for Clear), U3 (address search "No matches"), U4 (landmark name shown first when the geocoder returns one), U13 (place move dialog names the place).

### Task 8: Areas and geofences
flows D4 (save bar: content-width, offset by the sidenav, count visible, Cancel contrast ≥ 4.5:1), D5 (Areas card counts saved areas only), D15 (quiet chip only on saved areas), D9 (geofence name dialog shows why Save is disabled), D10 (import list refreshes however the results step closes), D11 (import says how many non-polygon features were skipped; a malformed file errors on the upload step), D12 (toggle/rename/profile-create failures show the server's reason), U6 (rename dialog title), cross D8 (area-chip quiet and remove buttons have accessible names).

### Task 9: Dashboard and app shell
flows D1 (resync snackbar names the new profile; copy "Your active profile is now "{{name}}". It was changed outside this tab."), D14 (location card on a 0,0 profile shows "Set your location" not 0.0000), D18 (no clipped pokéball decorations by section headers), cross D7 (location card icon not clipped), U9 (Weather card hidden when there's no data), U12 (status cards keyboard-reachable with role/tabindex), U1 (Quick Actions "Add Pokémon/Raid/Quest" open the add dialog on arrival, or are relabelled to their destination: prefer opening the dialog via a query param the list honours), flows D16 (login shows a generic message for an unknown `#error=` code), cross D9 (wrap the toolbar in `<header>`, add a skip link to main content, per-route `document.title` "<page> · <site title>", index.html `<title>` default "PoracleWeb.NET" instead of "DM Alerts", fix h1→h3 jumps on the listed pages, `role="dialog"` + label on the `?` overlay), cross D10 (44px touch targets on gen chips, quiet duration chips, profile filter chips, area chip buttons), cross note (a 429 on `/api/auth/me` doesn't surface as an uncaught console error).

### Task 10 (controller): bed check
Build the branch on dev-01, run it against the 5.4.0 bed with test users, and check each task's fixes in a real browser (Playwright), light and dark, desktop and phone. Retake any docs/Help screenshot whose page visibly changed (impersonation banner, admin settings, bulk delete toolbar, areas save bar, dashboard).
