---
paths:
  - "**/*Jwt*"
  - "**/AuthController*"
  - "**/auth.*"
  - "**/auth/**"
  - "**/*Auth*"
  - "**/BlockedAccountFilter*"
  - "**/AccountGone*"
  - "**/*Oidc*"
  - "**/oidc*"
  - "**/interceptors/**"
  - "**/Program.cs"
  - "**/SecurityHeaders*"
  - "**/token-store*"
  - "**/UserRoleResolver*"
  - "**/AdminController*"
---

## Rate Limiting
- Every policy is partitioned (never global), by one of two keys in `Program.cs`:
  - `IpPartitionKey` -- the client IP. Used by the anonymous endpoints, where there is no one else to key on.
  - `UserOrIpPartitionKey` -- the `userId` claim when the request carries a token, the IP otherwise. Used by
    everything authenticated, so people behind one shared NAT or corporate proxy do not share a bucket.
- `auth` policy: 30 requests per 60s **per IP** (login, callback, token exchange).
- `auth-read` policy: 120 requests per 60s **per user**, or per IP without a token (current user, profile switch, public settings).
- `test-alert` policy: 5 requests per 60s **per user**, or per IP without a token (test alert sends). It keys
  on `TestAlertPartitionKey`, not `UserOrIpPartitionKey`: an impersonation session counts against the
  impersonator's own bucket (the `impersonatedBy` claim), so a delegate managing several destinations gets
  five a minute in total, not five per destination (#1025).
- `mutes`, `geojson-import` and `scanner-search` are per user the same way; `geofence-feed-refresh` is per IP.
- `tracking-usage`: 20 per 60s **per user**, shared by `GET /api/location/pin-usage` and `GET /api/location/place-usage`.
  place-usage asks PoracleNG to describe every rule on the profile (tens of thousands on some accounts), so a loop is
  stopped; a person checking several places in a row never reaches it, and a 429 only drops the pre-check to the plain
  confirm (both URLs are in the error interceptor's silent list). `TrackingUsageRateLimitTests` holds both halves.
- `UseRateLimiter` runs **after** `UseAuthentication`, deliberately: registered before it, the per-user key
  could not see the claim and every "per-user" policy silently fell back to the IP (#581).
- **Important**: Never use global (non-partitioned) `AddFixedWindowLimiter` for auth -- multiple users sharing one bucket causes cascading login failures.

## Admin Self-Protection

An admin can neither block nor delete their **own** account: `AdminController.DisableUser` (#613) and `DeleteUser`
answer 400 "You cannot block your own account." / "You cannot delete your own account." for the caller's id, and
the Users page hides Block and Delete on the admin's own row (`isSelf`). Blocking takes the API away at once,
including the call that would unblock it, and the purge cannot be undone. Other admins and members stay blockable
and deletable; `AdminControllerTests` holds both halves.

## JWT Profile Desync
PoracleNG can change `current_profile_no` outside of PoracleWeb — the active-hours scheduler switches profiles on a cron, and bot commands like `!profile` update the DB directly. When this happens the JWT's `profileNo` claim goes stale, causing all alarm reads and writes to target the wrong profile. The `/api/auth/me` endpoint detects the mismatch by comparing the JWT claim against the live `current_profile_no` from `IHumanService` and returns a refreshed token (via the `Token` property on `UserInfo`) when they diverge. Frontend callers (`AuthService.loadCurrentUser`) must always check for and store the returned token.

## A Disabled Account's First 401 Is The One That Reaches Nobody

`AuthController.Me()` answers 401 for an admin-disabled account (`human.AdminDisable == 1`, not
impersonating -- see "Quiet Periods" for the unrelated per-area mute concept, and #706 for why
impersonation is exempt). That 401 lands mid-login: the Discord/OIDC/Telegram callback already minted a
token before `/api/auth/me` is ever called, so this is the first request to notice. `/api/auth/me` is in
`errorInterceptor`'s `SILENT_URL_PATTERNS` and `isAuthCallbackRoute()` is still true at that point in the
flow, so neither a toast nor the session-ending path fires -- the token survives, and
`AuthService.handleTokenFromCallback` would otherwise press on to `/dashboard` regardless of whether
`loadCurrentUser()` actually got a user back. The dashboard's own requests then 401 on that same dead
token, *outside* the callback route this time, and that is what the member actually sees: a generic
"session expired" toast, with no account anywhere explaining why. Every surface that does explain it
(the disabled banner, the Help FAQ, both from #910) sits behind the sign-in this 401 is refusing. See
#911.

The fix is a distinct machine-readable `code: "account_disabled"` on that one 401 body (not the deleted-
account 401 a row away, which stays prose-only and unhandled by any of this), carrying `supportUrl`
alongside it. `support_url` is otherwise read by signed-in callers only
(`SettingsController.UserVisibleKeys`) -- the one caller who needs it here is not signed in by the time
it matters, so the value travels inside the 401 body rather than through the normal settings fetch.
`AuthService` stores it in a private signal (`_disabledAccountInfo`) the instant `loadCurrentUser()` sees
the code, and `handleTokenFromCallback` reads it right after awaiting that call: if set, it clears the
session outright (the token is useless regardless) and sends the browser to `/login#error=account_disabled
&support_url=...` instead of `/dashboard`. This reuses the exact mechanism `LoginComponent` already has
for every other pre-token-issuance auth failure (`missing_required_role`, `not_in_guild`, etc.), just fed
by a failure that happens after a token exists rather than before one is issued.

**That redirect is a real `window.location.href` assignment, not `router.navigate({ fragment })` --
verified live in a real browser, not just jsdom.** Every existing `/login#error=...` is built by the
backend (`AuthController`'s `Redirect($"{frontendUrl}/login#error=...")`); this is the one place a
redirect like it is built client-side, and the first thing tried was the in-app Router's `fragment`
option. The Router's own fragment serialization re-escapes a literal `%`, so an already-percent-encoded
`support_url` (`...%3A%2F%2F...`, one `URLSearchParams.toString()` pass) came back out of
`router.navigate` double-encoded (`...%253A%252F%252F...`), and the link `LoginComponent` rendered
pointed at a broken, still-percent-encoded string instead of the real URL. Every unit test for this
passed regardless, because they mock `Router.navigate` and only assert what string it was called with --
none of them exercise the Router's actual URL-building. Caught by driving the real `CallbackComponent` →
`AuthService` → `Router` → `LoginComponent` chain in a live `ng serve` instance with Playwright,
intercepting `/api/auth/me` rather than mocking any application code. The one-encode/one-decode contract
(`URLSearchParams.toString()` written, `new URLSearchParams(location.hash)` read) now lives in its own
pure function, `disabledAccountFragment`, exported solely so it can be pinned by a fast unit test without
going near `window.location` or the Router at all.

`account_disabled` is deliberately not one of `LoginComponent`'s `errorKeys` map entries: that map renders
a one-line translated string, and this needs the same richer markup the dashboard banner already has
(`BANNER.DISABLED_ACCOUNT`'s bold "why", plus a conditional `BANNER.DISABLED_SUPPORT` link) -- both
already translated in all eleven locales as part of #910, reused verbatim rather than duplicated under a
new key.

## JWT Generation (IJwtService)
JWT token generation is centralized in `IJwtService` / `JwtService` (singleton). Three methods: `GenerateToken(UserInfo)` for fresh tokens, `GenerateImpersonationToken(UserInfo, impersonatedBy)` for admin impersonation, and `GenerateTokenWithReplacedProfile(ClaimsPrincipal, profileNo)` for profile switches. The latter filters out registered JWT claims (`exp`, `nbf`, `iat`, `iss`, `aud`) before copying to prevent stale claim duplication. All controllers (`AuthController`, `ProfileController`, `ProfileOverviewController`, `AdminController`) use this service — no inline JWT generation.

**A re-issue never moves `exp`, not even by a second.** `GenerateTokenWithReplacedProfile` backs four paths -- profile switch, `/api/auth/me` resync, overview duplicate and overview import -- and writes the original expiry instant back. It used to round the remaining lifetime up to whole minutes and count it from now, with a one-minute floor, so each re-issue pushed the expiry later and one a minute renewed a session forever; inside JwtBearer's five-minute clock skew the floor revived an expired token outright. That is how a revoked delegate kept an impersonation token alive. The only path that extends an ordinary session is OIDC refresh (`POST /api/auth/oidc/refresh`), which is intended: it spends a provider refresh token and re-validates the user live every time.

**An impersonation token is re-authorised on every request.** `ImpersonationAuthority`, hooked into JwtBearer's `OnTokenValidated`, asks `IUserRoleResolver` whether `impersonatedBy` is still an admin or still delegated the impersonated webhook, and fails authentication if not. The SPA answers that 401 by restoring the impersonator's own token. **The error interceptor judges each 401 by the bearer that request carried**, not by what is in storage when it lands: a page load sends about nine requests, so a revoked session comes back as a burst, and judging them against storage let the second 401 find the stash already spent and sign out the person just returned to their own account (#922). A 401 for a token that is no longer current, or for a request sent with none, is dropped silently. The refresh interceptor retries below the error interceptor, so it records the bearer a retry used (`TokenStoreService.noteRetriedWith`). Ending a session drops the tokens at once and clears the user only after the login navigation settles (`endSession`), the same order `logout()` uses, because clearing the user first swaps the shell's layout and rebuilds the page without a token. **An unresolved answer keeps the session** (fails open), for the #656 reason: one PoracleNG or `poracle_web` blip must not end every impersonation on the site. Because the resolver never caches an unresolved answer, an outage made every impersonated request re-ask every source and wait out its timeouts: measured with PoracleNG's address black-holed, ~200 s per request against 0.01 s for an ordinary session (a refused connection costs nothing). `ImpersonationRoleProbe`, a singleton used by this check alone, bounds it: a resolve gets two seconds, runs in its own DI scope so one that overruns cannot race the request's `DbContext`, is shared by concurrent requests, and once an answer comes back unresolved or late the impersonator is not re-asked for fifteen seconds, a pause that ends the moment a late resolve answers. Revocation therefore lands within fifteen seconds of the sources recovering, usually on the next request. The resolver's own cache is untouched, so its other consumers still never see a cached degraded answer.

**A Pokestop event sent to `/api/invasions` is refused where the events page exists.** Those rows live in the invasion table but the invasion list hides them, so the create answered 201 with a Location that 404s, or 200 with uid 0 beside an event rule the invasion guard (which reads the filtered list) could not see. The controller refuses with a pointer to Pokestop Events; service paths (quick picks, import, duplicate) still write them, since the rows are real and listed there.
