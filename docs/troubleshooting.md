# Troubleshooting

## Sign-in fails with "Invalid OAuth2 redirect_uri"

**Problem**: Clicking sign in sends you to Discord (or your OIDC provider) and it refuses with an invalid `redirect_uri`. Inspecting the URL shows the callback as `http://your-host/api/auth/discord/callback` even though the site is served over HTTPS.

**Solution**: You are behind a reverse proxy that PoracleWeb.NET has not been told to trust, so its `X-Forwarded-Proto: https` is discarded and the callback URL is built from the plain HTTP request the app actually received. The app logs a warning naming this when it happens — check the container logs to confirm.

The direct fix is to state the URL rather than let the app infer it:

```env
PUBLIC_URL=https://poracle.example.com
```

Also declare the proxy, which fixes the same problem at the source and additionally stops everyone behind it sharing a single rate-limit bucket:

```env
PROXY_KNOWN_NETWORKS=172.18.0.0/16,10.0.0.0/8
# or, for a single address:
# PROXY_KNOWN_PROXIES=127.0.0.1
```

Use the address the proxy connects **from**, as the container sees it. Then recreate the container: `docker compose up -d --force-recreate`.

Two things to check if it still comes back as `http://`:

- Your `docker-compose.yml` must actually pass the variable through. The shipped example uses `env_file: - .env`, which covers everything; a hand-edited file with an explicit `environment:` list needs `PROXY_KNOWN_PROXIES` and `PROXY_KNOWN_NETWORKS` added to it. Confirm with `docker inspect <container> --format '{{range .Config.Env}}{{println .}}{{end}}' | grep PROXY`.
- Your proxy must send `X-Forwarded-Proto`. Nginx needs `proxy_set_header X-Forwarded-Proto $scheme;` explicitly; Caddy and Cloudflare Tunnel send it by default.

The same misconfiguration also puts every user behind the proxy into a single rate-limit bucket. See [Behind a reverse proxy](getting-started/standalone-setup.md#reverse-proxy-optional).

---

## Container exits on startup: `Configuration 'Cors:AllowedOrigins' is required`

**Problem**: The container crash-loops on start. Logs show `System.InvalidOperationException: Configuration 'Cors:AllowedOrigins' is required in non-development environments.`

**Solution**: `CORS_ORIGIN` must be set to the URL you access PoracleWeb.NET from whenever `ASPNETCORE_ENVIRONMENT` is anything other than `Development` (Docker and systemd default to `Production`). Add it to `.env`:

```env
CORS_ORIGIN=http://your-server:8082
```

Then recreate the container: `docker compose up -d --force-recreate`. For reverse-proxied setups, use the public URL (e.g., `https://poracle.example.com`).

---

## PoracleNG unreachable (alarm operations fail)

**Problem**: All alarm operations (create, edit, delete, list) fail with HTTP 500 errors. The dashboard shows zero alarms. Logs show `HttpRequestException` or `TaskCanceledException` when calling the PoracleNG API.

**Solution**: Check that PoracleNG is running and reachable from the PoracleWeb.NET container:

1. Verify `Poracle:ApiAddress` points to the correct PoracleNG host and port
2. If using Docker, ensure both containers are on the same network or the PoracleNG port is exposed
3. Test connectivity: `curl http://<poracle-api-address>/api/config/poracleWeb` from inside the PoracleWeb.NET container
4. Verify `Poracle:ApiSecret` matches PoracleNG's `server.apiSecret` config value

!!! danger "All operations go through PoracleNG"
    Unlike previous versions where PoracleWeb.NET wrote directly to MySQL, all alarm tracking, user registration, location setting, area management, and profile switching now require a running PoracleNG instance. If PoracleNG is down, users cannot create/edit/delete/view alarms, register, set their location, update areas, or switch profiles. Only admin bulk operations (list all users, delete user) use direct DB access.

---

## Stale alarm state after changes

**Problem**: Users report that alarm changes (create/delete) are not reflected in notifications. Alarms appear correct in the web UI but PoracleNG seems to use old data.

**Solution**: Check PoracleNG logs for state reload errors. PoracleNG reloads its in-memory state after every tracking mutation. If the reload fails (e.g., due to a NULL column in the database), PoracleNG continues running with stale data.

Common causes:

- NULL values in `template` or `ping` columns from historical direct-write bugs. Fix with: `UPDATE monsters SET template = '1' WHERE template IS NULL`
- PoracleNG's `monsters.go` query lacks `COALESCE` for `template` and `ping` (known bug). The fix is to add `COALESCE(template, '1') AS template` to the query.

---

## Webhook user operations fail with 404

**Problem**: Alarm or human operations fail with HTTP 404 for webhook users. Logs may show mangled URL paths like `/api/tracking/pokemon/https://discord.com/api/webhooks/123/abc`.

**Solution**: Webhook user IDs are full URLs containing slashes. Both `PoracleTrackingProxy` and `PoracleHumanProxy` must URL-encode user IDs with `Uri.EscapeDataString()` before inserting them into URL paths. If you add a new proxy method, always use the `Encode()` helper.

---

## uid:0 causes updates instead of creates

**Problem**: Creating a new alarm silently updates an existing alarm instead of creating a new one, or PoracleNG returns an error about invalid UID.

**Solution**: PoracleNG treats `uid=0` in a create request body as an update target. C# model `int` properties default to `0`, so freshly constructed models include `"uid":0` when serialized. `PoracleJsonHelper.SerializeToElement()` automatically strips `"uid":0` from request bodies. If you serialize alarm data manually (bypassing the helper), ensure you remove the `uid` property when its value is `0`.

---

## PoracleNG response wrapper not unwrapped

**Problem**: Human data appears as `null` or deserialization fails even though the PoracleNG API returns a 200 response.

**Solution**: PoracleNG wraps certain responses in container objects. For example, `GET /api/humans/one/{id}` returns `{ "human": { ... }, "status": "ok" }`, not the human object directly. `PoracleHumanProxy.GetHumanAsync()` unwraps the `"human"` property. If you add new proxy endpoints, inspect the actual PoracleNG response shape and unwrap accordingly.

---

## snake_case deserialization issues

**Problem**: Alarm data appears empty or fields are null/zero even though alarms exist in the database.

**Solution**: PoracleNG returns alarm data in snake_case JSON (`pokemon_id`, `min_iv`, `max_cp`). PoracleWeb.NET deserializes this with `JsonNamingPolicy.SnakeCaseLower`. If a field is not deserializing correctly:

1. Check that the C# model property name matches the snake_case convention (e.g., `PokemonId` maps to `pokemon_id`)
2. Verify `PropertyNameCaseInsensitive = true` is set on the `JsonSerializerOptions`
3. Compare the actual JSON response from PoracleNG (`GET /api/tracking/{type}/{userId}`) against the expected field names

---

## MySQL provider incompatibility

**Problem**: Build errors or runtime exceptions related to `Pomelo.EntityFrameworkCore.MySql`.

**Solution**: This project uses `MySql.EntityFrameworkCore` (Oracle's official provider), **not** Pomelo. Pomelo is incompatible with EF Core 10. Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

---

## NULL string constraint violations

**Problem**: MySQL errors like `Column 'X' cannot be null` when saving entities.

**Solution**: For alarm entities, this should no longer occur since writes go through the PoracleNG API (which handles NULL defaults). For non-alarm entities (`humans`, `profiles`), repositories handle null normalization as needed. Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`.

---

## Discord API calls failing

**Problem**: Discord API calls return errors or time out.

**Solution**: Use `discordapp.com` (not `discord.com`) for API calls. The `discord.com` domain is blocked by Cloudflare in some server environments. PoracleWeb.NET is already configured to use `discordapp.com`.

Also note: Use API **v9** (not v10) — v10 is not supported on the `discordapp.com` domain.

---

## Poracle config defaultTemplateName errors

**Problem**: Deserialization errors when parsing Poracle config.

**Solution**: `defaultTemplateName` can be a number (e.g., `1`) or a string (e.g., `"default"`). Use `JsonElement` or handle both types during deserialization.

---

## Scanner DB connection errors

**Problem**: Errors about missing scanner service or database connection.

**Solution**: The `ScannerDb` connection string is optional. If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses. This is expected behavior.

---

## Bulk update zeroing out alarm fields

**Problem**: After bulk updating alarms, fields like `clean`, `template`, and filter settings are reset to 0.

**Solution**: Alarm updates are now proxied through PoracleNG, which applies `cleanRow()` defaults. However, it is still important to send the full alarm object when updating. The frontend should spread the full alarm: `{ ...alarm, distance }`. The dedicated `PUT /distance/bulk` endpoint handles this correctly by fetching all alarms, modifying only the distance, and POSTing back.

---

## Geofence names not matching in Poracle

**Problem**: Custom geofences don't trigger alerts even though they're in the user's area list.

**Solution**: Poracle does **case-sensitive** area matching. Geofence names must always be lowercase. The `kojiName` field in `user_geofences` and entries in `humans.area` must match exactly. `UserGeofenceService.CreateAsync()` enforces this with `ToLowerInvariant()`.

---

## Koji displayInMatches not working

**Problem**: User geofence names appear in DMs even though `displayInMatches` is set to `false`.

**Solution**: Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. Serve user geofences from the PoracleWeb.NET feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

---

## Rate limiting locking out all users

**Problem**: Multiple users report being unable to log in simultaneously.

**Solution**: Auth rate limiting must be **per-IP** (partitioned), not global. Check that `Program.cs` uses `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`, not `AddFixedWindowLimiter`.

---

## gym_id NULL vs empty-string mismatch

**Problem**: Gym alarms don't match any gyms even though no specific gym is selected. The `gym_id` column contains `''` (empty string) instead of `NULL`.

**Solution**: This was caused by direct database writes. New alarms created through the PoracleNG API proxy have correct `gym_id` NULL handling. The SQL fix below is only needed for alarms created before the migration. Poracle treats `gym_id = ''` as "track a specific gym with an empty ID," which matches nothing.

To fix existing data:

```sql
UPDATE gym SET gym_id = NULL WHERE gym_id = '';
UPDATE egg SET gym_id = NULL WHERE gym_id = '';
UPDATE raid SET gym_id = NULL WHERE gym_id = '';
```

---

## GymCreate.Team defaults to 0 (Neutral only) — legacy

!!! note "Legacy issue"
    This was caused by direct database writes. New alarms created through the PoracleNG API proxy have correct defaults applied by `cleanRow()`. The SQL fixes below are only needed for alarms created before the migration.

**Problem**: New gym alarms created via the web UI only match Neutral (team 0) gyms instead of all teams.

**Solution**: C# `int` defaults to `0`, which in Poracle means "Neutral only." `GymCreate.Team` must default to `4` (any team), matching `RaidCreate` and `EggCreate`. This was fixed in v1.1.2. If users created gym alarms before the fix, update them:

```sql
-- Fix gym alarms that are stuck on Neutral-only due to missing default
UPDATE gym SET team = 4 WHERE team = 0;
```

---

## Gym alerts not working

**Problem**: Users report that gym alarms are not triggering any notifications.

**Solution**: This is typically caused by the `gym_id` column containing an empty string instead of `NULL`. When `gym_id = ''`, Poracle interprets it as tracking a specific gym with an empty ID, which matches nothing. Additionally, check that `team` is not `0` (Neutral only) when the user intended to track all teams.

Diagnostic queries:

```sql
-- Check for empty-string gym_id (should be NULL for "any gym")
SELECT uid, id, gym_id, team FROM gym WHERE gym_id = '';

-- Check for team=0 (Neutral only) when it should be 4 (any team)
SELECT uid, id, gym_id, team FROM gym WHERE team = 0;
```

Fix:

```sql
UPDATE gym SET gym_id = NULL WHERE gym_id = '';
UPDATE gym SET team = 4 WHERE team = 0;
```

---

## Monster filter defaults (size, max_level, etc.) — legacy

!!! note "Legacy issue (PoracleJS only)"
    This was caused by direct database writes with incorrect C# model defaults in early versions of PoracleWeb.NET. New alarms created through the PoracleNG API proxy have correct defaults applied by PoracleNG itself. The SQL queries below help diagnose alarms created before the migration, on PoracleJS installations.

**Problem**: On PoracleJS, monster alarms created by old versions of PoracleWeb.NET may silently filter out pokemon if model defaults don't match PoracleJS expectations. For example, `max_size=0` causes all pokemon with size data to be rejected, and `size=0` instead of `size=-1` shows incorrectly in the old PHP UI as "-XXL". This does not apply to PoracleNG, which applies its own defaults on every write.

**Solution**: All Create model defaults are aligned with the PHP PoracleWeb.NET `include/defaults.php`. Key values:

- `size=-1` means "no size filter" (not `0`)
- `max_size=5` means "up to XXL"
- `max_level=55` (not 40 or 50)
- Raid/Egg `team=4` means "all teams"
- Raid `move=9000` and `evolution=9000` mean "no filter"

If users report missing alerts, check the `monsters` table for rows where max fields are `0` when they should have defaults:

```sql
-- Find alarms with broken size filter (rejects all pokemon with size data)
SELECT * FROM monsters WHERE max_size = 0;

-- Find alarms with incorrect "no size filter" value (shows as "-XXL" in PHP UI)
SELECT * FROM monsters WHERE size = 0;
```

---

## Profile switches at wrong time

**Problem**: Auto-profile switches happen hours earlier or later than the configured active hours schedule.

**Solution**: The profile has `0,0` coordinates, so PoracleNG's scheduler falls back to UTC instead of the user's local timezone. Set the pin on the affected profile via the Dashboard or the Areas & Places page. The Profiles page shows a red location warning banner on profiles that have no coordinates set.

---

## Active hours not showing on profiles

**Problem**: All profiles display "Manual only" even though active hours were configured via the Discord bot.

**Solution**: PoracleWeb.NET reads active hours from PoracleNG's profile API responses — they should appear automatically. If they don't:

1. Verify PoracleNG is reachable (`Poracle:ApiAddress`)
2. Check that PoracleNG is returning `active_hours` in profile responses (`GET /api/humans/one/{id}` should include profile data with `active_hours`)
3. If active hours were set via `$!profile` bot commands, they are stored in the same place PoracleWeb.NET reads from — no separate sync is needed

---

## Schedule changes don't take effect

**Problem**: After saving active hours in PoracleWeb.NET, the profile doesn't auto-switch at the expected time.

**Solution**: PoracleNG's profile scheduler checks on a periodic cycle (every few minutes) with a 10-minute matching window. Changes saved in PoracleWeb.NET are written to PoracleNG immediately, but the scheduler picks them up on its next cycle. Wait up to 10 minutes for changes to take effect.

!!! note "PoracleNG owns the scheduler"
    PoracleWeb.NET only manages the active hours data. The actual profile switching logic runs in PoracleNG's processor. If auto-switching isn't working at all, check PoracleNG's logs for scheduler errors.

---

## Pokemon availability not showing

**Symptom**: The "Live > Spawning" filter doesn't appear in the Pokemon selector.

**Causes and fixes**:

1. **Golbat not configured**: Set `GOLBAT_API_ADDRESS` and `GOLBAT_API_SECRET` in `.env` and restart the container. The feature is only enabled when both are set.

2. **Env vars not reaching the container**: The compose file loads configuration via `env_file: .env`, so any var defined in `.env` is automatically passed in — no per-key entries in `docker-compose.yml` are needed. If you've customized your `docker-compose.yml` (a local copy of `docker-compose.yml.example`), confirm the `env_file: .env` line is still present under the app service.

3. **Golbat API unreachable from container**: Verify connectivity from inside the container. Check the app logs for `Failed to fetch available Pokemon from Golbat API` warnings.

4. **Wrong API secret**: The `GOLBAT_API_SECRET` must match Golbat's `api_secret` value in its `config.toml`. An incorrect secret results in `401 Unauthorised` responses.

5. **Browser cache**: Hard refresh (Ctrl+Shift+R) after deploying a new build. The old JavaScript bundle won't have the availability code.

**Diagnostic**:
```bash
# Test Golbat API from host
curl -H "X-Golbat-Secret: YOUR_SECRET" http://GOLBAT_HOST:9001/api/pokemon/available

# Check if env vars reached the container
docker exec poracleweb.net printenv | grep -i golbat

# Check app logs for Golbat activity
docker logs poracleweb.net 2>&1 | grep -i golbat
```

---

## Quest summary delivery menu is missing

**Problem**: The **Quest summary delivery** item does not appear in the Quests page **⋮** menu.

**Solution**: The menu is shown only when the connected PoracleNG instance reports quest summaries as enabled. PoracleWeb.NET reads the effective `tracking.quest_summary_enabled` flag from PoracleNG's `/api/config/values` endpoint (cached for five minutes). If the menu is missing:

1. **Enable the feature on the bot**: set `quest_summary_enabled = true` under `[tracking]` in PoracleNG's `config.toml` and restart the processor.
2. **Make sure the processor API is reachable**: PoracleWeb.NET must be able to reach PoracleNG over HTTP. If they run on different machines or in separate containers, set `host = "0.0.0.0"` (or the LAN IP) under `[processor]` in PoracleNG's config — the `127.0.0.1` default refuses off-box connections.
3. **Wait out the cache / hard refresh**: the capability is cached for five minutes; reload the Quests page (Ctrl+Shift+R) after enabling.

!!! note
    A transient `503` from PoracleNG's summary endpoints is treated as a temporary backend fault, **not** as "feature off." The feature flag is read from the config endpoint, not inferred from a 503.

---

## Send summary now delivers nothing

**Problem**: Pressing **Send summary now** succeeds but no summary DM arrives.

**Solution**: Send summary now flushes only the quests PoracleNG has **buffered** since your last summary. An empty buffer delivers nothing — which is expected, not an error. To buffer quests:

1. **Enable Daily summary on at least one quest alarm** (the per-alarm toggle in the quest add/edit dialog). Only alarms with this toggle are buffered; the rest deliver immediately.
2. **Confirm the feature is enabled on the bot** (`tracking.quest_summary_enabled = true`) — when it is off, PoracleNG's matcher does not buffer at all, so the buffer stays empty.
3. **Give it time**: quests are buffered as they match. Right after enabling the feature, or after a summary fires, the buffer starts empty and fills as matching quests come in. PoracleNG's status log shows the current count (`Summary: N buffered`).

---

## External SSO / OIDC login

The issues below cover the generic external OIDC/OAuth2 login provider. For the full settings reference, see [External SSO](configuration/external-sso.md); for the silent-refresh feature, see [OIDC Refresh Tokens](configuration/oidc-refresh-tokens.md).

### "External login failed" / 405 on the token or userinfo call

**Problem**: The OIDC login starts, the user authenticates at the provider, but the callback redirects to `/login#error=oidc_token_exchange_failed` (or `oidc_userinfo_failed`). The provider's logs show a `405 Method Not Allowed` on the token or userinfo request.

**Solution**: Some providers are *split-host* — the browser-facing authorize endpoint lives on one host (the frontend/login host) while the token and userinfo endpoints live on a separate API host. Pointing `OIDC_TOKEN_URL` / `OIDC_USERINFO_URL` at the frontend host hits a static site with no POST handler, which returns `405`.

Set each endpoint to its correct host:

```env
OIDC_AUTHORIZATION_URL=https://login.provider.example/oauth2/authorize
OIDC_TOKEN_URL=https://api.provider.example/oauth2/token
OIDC_USERINFO_URL=https://api.provider.example/oauth2/userinfo
```

Only `OIDC_AUTHORIZATION_URL` belongs on the frontend host; `OIDC_TOKEN_URL` and `OIDC_USERINFO_URL` go to the API host.

---

### redirect_uri mismatch / invalid redirect

**Problem**: The provider rejects the login with an "invalid redirect URI" or "redirect_uri mismatch" error before the user ever reaches PoracleWeb.NET's callback.

**Solution**: PoracleWeb.NET builds the callback URL as `{scheme}://{Host}/api/auth/oidc/callback` from the **incoming request Host header**, and that exact URL must be registered at the IdP. In local development the Angular dev-server proxy preserves `Host = localhost:4201`, so the callback becomes `:4201`, not the API's `:5048`. Register every host that can originate the request as an allowed redirect URI at the provider:

```text
http://localhost:5048/api/auth/oidc/callback
http://localhost:4201/api/auth/oidc/callback
https://poracle.example.com/api/auth/oidc/callback
```

Include your real production host alongside the two local-dev URIs.

---

### 404 after OIDC login in local dev (standalone `ng serve`)

**Problem**: Running the Angular dev server standalone, OIDC login completes at the provider but the browser lands on a 404 instead of the dashboard.

**Solution**: The callback issues a `302` to the Angular client route `/auth/oidc/callback#token=…`. The committed `proxy.conf.json` proxies `/auth` to the API, so the dev server forwards that client route to the API (which has no such route) → `404`. Run the dev server with an `/api`-only proxy so Angular serves `/auth/*` itself:

```json
{
  "/api": { "target": "http://localhost:5048", "secure": false }
}
```

A ready-made `proxy.local.json` is provided for this; start the dev server with `ng serve --proxy-config proxy.local.json`.

!!! note "Only affects standalone `ng serve`"
    When the API serves the built SPA (Docker, production), there is no separate dev-server proxy and Angular's router handles `/auth/oidc/callback` directly — this issue does not occur.

---

### `#error=user_not_registered` at the callback

**Problem**: Login succeeds at the provider but the browser returns to `/login#error=user_not_registered`.

**Solution**: The value carried by the configured identity claim has no matching row in the Poracle `human` table — the SSO user has no Poracle account. PoracleWeb.NET reads `OIDC_IDENTITY_CLAIM` (default `discord_id`, falling back to the standard `sub` claim) and looks that value up as the Poracle human id. To fix:

1. Ensure the claim carries the user's Poracle id — a linked Discord or Telegram id, not an internal SSO/email id.
2. Confirm a matching user actually exists in Poracle (they must have registered with the bot).

!!! note "PogoAlerts users must link Discord"
    At PogoAlerts the user must have Discord linked to their account so the provider emits the `discord_id` claim. Without a linked Discord, no `discord_id` is sent and the lookup fails.

---

### Silent refresh not happening / no refresh token issued

**Problem**: Sessions still expire at the full JWT lifetime instead of refreshing silently. The app logs *"OIDC refresh tokens are enabled but the provider returned no refresh token (offline_access not granted?); falling back to a standard session."*

**Solution**: Standards-compliant providers only issue a refresh token when the `offline_access` scope is requested and granted. If `OIDC_OFFLINE_ACCESS_SCOPE` is blanked (or the provider declined to grant it), the token response carries no refresh token, and PoracleWeb.NET gracefully falls back to a normal full-lifetime session — no error is shown to the user.

1. Set `OIDC_USE_REFRESH_TOKENS=true`.
2. Ensure the provider actually issues refresh tokens: leave `OIDC_OFFLINE_ACCESS_SCOPE=offline_access` (the default) so the scope is requested, or use the provider's own mechanism (e.g. Google's `?access_type=offline`).

See [OIDC Refresh Tokens](configuration/oidc-refresh-tokens.md) for the full setup.

---

### Token endpoint returns 400/401 invalid_client

**Problem**: The token exchange fails with the provider returning `400`/`401` and an `invalid_client` error, so the callback redirects to `/login#error=oidc_token_exchange_failed`.

**Solution**: `OIDC_TOKEN_AUTH_METHOD` must match how the provider expects client credentials presented:

- `client_secret_post` — credentials sent in the request **body**.
- `client_secret_basic` — credentials sent in the HTTP **Basic** `Authorization` header.

Match the provider's expectation: Keycloak and Okta default to `client_secret_basic`; Auth0, Azure, and PogoAlerts use `client_secret_post`.

---

### Locked out after switching to SSO (provider down / misconfigured)

**Problem**: An admin set `enable_oidc` to SSO mode, the login page now auto-redirects to the provider, and the provider is down or misconfigured — nobody can sign in to fix it.

**Solution**: This is the break-glass scenario. Set the env flag and restart:

```env
AUTH_FORCE_LOCAL=true
```

This forces the local login page regardless of the `enable_oidc` mode, so an admin can sign in and disable or repair the OIDC configuration. Once fixed, remove the flag and restart.

!!! note "Admins can always reach local login"
    Even when `enable_oidc` is off (or a provider is broken), admins can always reach the local login page — the `enable_oidc` gate is enforced *after* authentication so an admin is never locked out of re-enabling or fixing the setting.

---

### "Sign out everywhere" 404s / single logout doesn't end the provider session

**Problem**: The "Sign out everywhere" option is missing, returns a 404, or signs the user out of PoracleWeb.NET but leaves the provider session active (so the next login skips re-authentication).

**Solution**: RP-initiated single logout requires all three of:

1. **`OIDC_END_SESSION_URL` configured** — without it, logout falls back to a plain local sign-out.
2. **`enable_oidc_slo` not set to `false`** — this admin runtime toggle defaults to on once the end-session URL is wired; an explicit `false` disables single logout.
3. **The `post_logout_redirect_uri` registered at the IdP** — PoracleWeb.NET sends `{origin}/login?loggedout=1`. If that URL isn't in the provider's allow-list, the provider rejects the logout redirect.

Configure the end-session URL, leave `enable_oidc_slo` unset (or `true`), and register the post-logout redirect URI at the IdP.
