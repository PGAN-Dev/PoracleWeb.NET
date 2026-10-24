# Troubleshooting

## SSO users are signed out every time the container restarts

**Problem**: The log starts with `DATA_DIR /app/data is not writable`, or with a DataProtection
`Access to the path '/app/data/dataprotection-keys' is denied` error.

**Solution**: Docker created the bind-mounted `./data` as root, and the container runs as uid 999. Give
the directory to that user and restart:

```bash
sudo chown -R 999:999 ./data
docker compose up -d --force-recreate
```

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

**Problem**: Every alarm page shows "The server is temporarily unavailable" instead of its list, and adds, edits and deletes fail the same way. The API answers 503 with `{"error": "PoracleNG could not be reached. Try again shortly."}`. Logs show `HttpRequestException` or `TaskCanceledException` when calling the PoracleNG API.

**Solution**: Check that PoracleNG is running and reachable from the PoracleWeb.NET container:

1. Verify `Poracle:ApiAddress` points to the correct PoracleNG host and port
2. If using Docker, ensure both containers are on the same network or the PoracleNG port is exposed
3. Test connectivity: `curl http://<poracle-api-address>/api/config/poracleWeb` from inside the PoracleWeb.NET container
4. Verify `Poracle:ApiSecret` matches PoracleNG's `server.apiSecret` config value

!!! danger "All operations go through PoracleNG"
    Unlike previous versions where PoracleWeb.NET wrote directly to MySQL, all alarm tracking, user registration, location setting, area management, and profile switching now require a running PoracleNG instance. If PoracleNG is down, users cannot create/edit/delete/view alarms, register, set their location, update areas, or switch profiles. This includes the admin user list and user deletion, which use PoracleNG's `/api/v2` routes.

---

## Stale alarm state after changes

**Problem**: Users report that alarm changes (create/delete) are not reflected in notifications. Alarms appear correct in the web UI but PoracleNG seems to use old data.

**Solution**: Check PoracleNG logs for state reload errors. PoracleNG reloads its in-memory state after every tracking mutation. If the reload fails (e.g., due to a NULL column in the database), PoracleNG continues running with stale data.

The historical cause is a NULL `template` left behind by a pre-v2.0.0 direct write. PoracleNG's loader
scans that column into a non-nullable Go field, so a single NULL row aborts the whole reload. Commit
`e81b629` added `COALESCE(template, '')` to the loaders, and every build from 5.1.0 onward carries it,
so this only bites a PoracleNG older than the supported minimum. On one of those, clear the data:

```sql
UPDATE monsters SET template = '1' WHERE template IS NULL;
```

Repeat for the other tracking tables if their reloads fail too. Nothing PoracleWeb.NET writes today can
produce this — every alarm write goes through the PoracleNG API, which fills the column itself.

---

## Webhook user operations fail with 404

**Problem**: Alarm or human operations fail with HTTP 404 for webhook users. Logs may show mangled URL paths like `/api/v2/humans/https://discord.com/api/webhooks/123/abc/tracking/pokemon`.

**Solution**: Webhook user IDs are full URLs containing slashes. Both `PoracleTrackingProxy` and `PoracleHumanProxy` must URL-encode user IDs with `Uri.EscapeDataString()` before inserting them into URL paths. If you add a new proxy method, always use the `Encode()` helper.

---

## uid:0 causes updates instead of creates

This was a v1 problem. PoracleNG's v1 create read `uid=0` as an update target, and a freshly built C# model serialises `"uid":0`. `PoracleJsonHelper.SerializeToElement()` still strips it, and every write now goes through the v2 translator, which never sends `uid` in a rule body, so it can't happen on a current build.

---

## PoracleNG response wrapper not unwrapped

**Problem**: Human data appears as `null` or deserialization fails even though the PoracleNG API returns a 200 response.

**Solution**: PoracleNG wraps certain responses in container objects. For example, `GET /api/v2/humans/{id}` returns `{ "human": { ... } }`, not the human object directly. `PoracleHumanProxy.GetHumanAsync()` unwraps the `"human"` property. If you add new proxy endpoints, inspect the actual PoracleNG response shape and unwrap accordingly.

---

## snake_case deserialization issues

**Problem**: Alarm data appears empty or fields are null/zero even though alarms exist in the database.

**Solution**: PoracleNG returns alarm data in snake_case JSON (`pokemon_id`, `min_iv`, `max_cp`). PoracleWeb.NET deserializes this with `JsonNamingPolicy.SnakeCaseLower`. If a field is not deserializing correctly:

1. Check that the C# model property name matches the snake_case convention (e.g., `PokemonId` maps to `pokemon_id`)
2. Verify `PropertyNameCaseInsensitive = true` is set on the `JsonSerializerOptions`
3. Compare the actual JSON response from PoracleNG (`GET /api/v2/humans/{id}/tracking/{type}`) against the expected field names. The proxy restates v2's rules in the v1 row shape before any service sees them, so check `TrackingV2Translator.ToV1Row` too

---

## MySQL provider incompatibility

**Problem**: Build errors or runtime exceptions related to `Pomelo.EntityFrameworkCore.MySql`.

**Solution**: This project uses `MySql.EntityFrameworkCore` (Oracle's official provider), **not** Pomelo. Pomelo is incompatible with EF Core 10. Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

---

## PoracleNG is older than 5.4.0

**Symptoms**: the Versions card under **Admin > Server settings** reports a PoracleNG version below 5.4.0. Alarm, profile, area or place calls fail with a message like "This PoracleNG does not support listing areas. It requires PoracleNG 5.4.0." The app itself still starts, and logs an error naming the mismatch.

**Fix**: upgrade PoracleNG to 5.4.0 or newer, then reload the page. Nothing in PoracleWeb.NET needs changing. See [Upgrading to PoracleNG 5.4.0](getting-started/upgrading-to-poracleng-5-4.md).

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

**Problem**: After a bulk change, fields like `clean`, `template` and the filters are reset.

**Solution**: Not possible on a current build. Bulk distance and the cleaning toggle send one `PATCH /api/v2/humans/{id}/tracking/{type}` carrying the uids and the one setting (`{"uids": [...], "set": {"distance": n}}`), and PoracleNG leaves everything the set doesn't name as stored. A single edit is different: `PUT /api/v2/humans/{id}/tracking/{type}/{uid}` is a full replace, which is why `TrackingFieldPreserver` merges the stored rule into the body first. If a single edit loses a field, look there.

---

## An area is missing from Areas & places

**Problem**: An area exists in Koji and alerts still fire from it, but nobody can find it on
**Areas & places** — admins included.

**Solution**: Something is serving it as `userSelectable: false`, and
`GET /api/areas/available` drops those for every caller. Two things can do that:

1. **The hidden list on this site.** Check **Admin → Areas**; the switch beside the area says whether
   you hid it. That page is the only one that shows hidden areas, which is why it exists — the normal
   Areas page cannot show you what it is hiding.
2. **Koji itself.** An area set non-public in Koji arrives with the flag already cleared. **Admin →
   Areas** lists those as *Private in Koji* with the switch disabled, because clearing a flag this site
   did not set would not bring it back. Fix it in Koji.

Until v2.19.0 this site read Koji's export but forced both visibility flags to `true`, so an area set
private in Koji kept appearing here. If you are on an older build, that is why.

A change in either place takes up to five minutes (the Koji fetch is cached) plus a Poracle geofence
reload. Saving on **Admin → Areas** asks Poracle to reload immediately; if it could not, the page says
so rather than claiming success.

---

## An area I hid is still sending alerts

**Problem**: You hid an area, but people who had already selected it keep receiving its notifications.

**Solution**: Working as intended. Hiding controls the pickers, not matching. Poracle never consults
`userSelectable` when deciding which fences a spawn fell in, so a profile still carrying the name keeps
matching it, and it keeps carrying it: saving on **Areas & places** sends back every area the profile
already holds, hidden ones included, and an alarm limited to the area keeps it when edited. A user
leaves a hidden area only by removing it themselves. If you need people off an area, tell them; see
[Hiding an area from your users](features/custom-geofences/koji-and-regions.md#hiding-an-area-from-your-users).

---

## A geofence saved but not switched on

**Problem**: After drawing a geofence the user sees "Saved, but it couldn't be switched on yet. Use the toggle to retry."

**Solution**: The geofence row is saved and PoracleNG then refused or missed the name on the user's profile, most often because PoracleNG was unreachable or hadn't reloaded its geofences yet. The user flips the toggle on the Geofences page to retry (it reloads PoracleNG's geofences and retries once; a second failure answers a 409). Check that PoracleNG is up and that `geofence.path` points at `/api/geofence-feed`. See also [Custom geofences troubleshooting](features/custom-geofences/troubleshooting.md#a-geofence-saved-but-not-switched-on).

---

## Geofence names not matching in Poracle

**Problem**: Custom geofences don't trigger alerts even though they're in the user's area list.

**Solution**: Poracle does **case-sensitive** area matching. Geofence names must always be lowercase. The `kojiName` field in `user_geofences` and entries in a profile's area list must match exactly. `UserGeofenceService.CreateAsync()` enforces this with `ToLowerInvariant()`.

---

## Koji displayInMatches not working

**Problem**: User geofence names appear in DMs even though `displayInMatches` is set to `false`.

**Solution**: Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. Serve user geofences from the PoracleWeb.NET feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

---

## Rate limiting locking out all users

**Problem**: Multiple users report being unable to log in simultaneously.

**Solution**: Auth rate limiting must be **per-IP** (partitioned), not global. Check that `Program.cs` uses `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`, not `AddFixedWindowLimiter`.

---

## Gym, raid or egg alarms never fire

**Problem**: Gym alarms match nothing, even though no specific gym was selected.

**Solution**: `gym_id` is nullable, and PoracleNG reads any non-NULL value — including `''` — as "only
this gym", which an empty string matches nowhere. Alarms created through the PoracleNG API proxy get
this right; the rows that need fixing were written directly to the database by PoracleWeb.NET before
v2.0.0.

Old gym rows can also carry `team = 0`, which means Neutral only rather than any team.

```sql
-- Diagnose
SELECT uid, id, gym_id, team FROM gym WHERE gym_id = '' OR team = 0;

-- Fix
UPDATE gym SET gym_id = NULL WHERE gym_id = '';
UPDATE egg SET gym_id = NULL WHERE gym_id = '';
UPDATE raid SET gym_id = NULL WHERE gym_id = '';
UPDATE gym SET team = 4 WHERE team = 0;
```

---

## Profile switches at wrong time

**Problem**: Auto-profile switches happen hours earlier or later than the configured active hours schedule.

**Solution**: The profile has `0,0` coordinates, so PoracleNG's scheduler falls back to UTC instead of the user's local timezone. Set the pin on the affected profile via the Dashboard or the Areas & places page. The Profiles page shows a red location warning banner on profiles that have no coordinates set.

---

## Active hours not showing on profiles

**Problem**: All profiles display "Manual only" even though active hours were configured via the Discord bot.

**Solution**: PoracleWeb.NET reads active hours from PoracleNG's profile API responses — they should appear automatically. If they don't:

1. Verify PoracleNG is reachable (`Poracle:ApiAddress`)
2. Check that PoracleNG is returning `active_hours` in profile responses (`GET /api/v2/humans/{id}/profiles` should list each profile with its `active_hours`)
3. If active hours were set via `$!profile` bot commands, they are stored in the same place PoracleWeb.NET reads from — no separate sync is needed

---

## Schedule changes don't take effect

**Problem**: After saving active hours in PoracleWeb.NET, the profile doesn't auto-switch at the expected time.

**Solution**: PoracleNG's profile scheduler checks on a periodic cycle (every few minutes) with a 10-minute matching window. Changes saved in PoracleWeb.NET are written to PoracleNG immediately, but the scheduler picks them up on its next cycle. Wait up to 10 minutes for changes to take effect.

!!! note "PoracleNG owns the scheduler"
    PoracleWeb.NET only manages the active hours data. The actual profile switching logic runs in PoracleNG's processor. If auto-switching isn't working at all, check PoracleNG's logs for scheduler errors.

---

## Maps are watermarked, blocked, blank, or ignoring the setting

**Symptom**: Every map on the site — areas, the location picker, geofence thumbnails — shows one of:
diagonal `API KEY REQUIRED` text, black-and-yellow `403 Access blocked` tiles, nothing at all, or a
basemap that is not the one configured.

!!! warning "A tile provider refusing you still answers HTTP 200"
    Both CARTO and OpenStreetMap draw their refusal *into the image* and return success. Nothing
    appears in the container logs, no health check fails, and the browser's network tab shows 200s.
    The only place these faults are visible is on the map itself, so do not go looking for an error.

**Causes and fixes**:

1. `API KEY REQUIRED` across the tiles. A CARTO or Stadia basemap is being requested without a
   key. Set **Basemap API key** in **Admin > Server settings > Maps**. Takes effect on the next page load,
   no restart.

2. Still watermarked after setting a key. The key is wrong, not missing. The site can detect an
   absent key and avoid the request; it cannot detect an invalid one, because the response is
   identical. Check the key itself, and — for a custom tile URL — check the parameter name. CARTO's
   is `key`, not `api_key`; the wrong name also returns 200 and also watermarks.

3. `403 Access blocked — App is not following the tile usage policy`. OpenStreetMap refuses
   requests it cannot identify. The built-in OpenStreetMap entry handles this by sending the site's
   origin on its tile images. If you see this, you are on a **custom tile URL pointing at
   `tile.openstreetmap.org`** rather than the built-in entry — switch the provider to
   **OpenStreetMap**, which carries the required behaviour.

4. Blank map, no tiles, no message. A custom tile URL over plain `http://`. The site's
   Content-Security-Policy allows images from `https:` only, so the browser blocks them silently.
   Serve your tiles over HTTPS.

5. Blank map with a custom URL over HTTPS. Check the template. It must be an absolute URL
   containing Leaflet's `{z}`, `{x}` and `{y}` placeholders. A URL that is not a tile template is
   never requested at all, and the site falls back to OpenStreetMap and says so in the Maps section.

6. The basemap is not the one you configured. You have picked a different one for yourself. Each
   viewer's choice is stored per-browser and outranks the site setting, so an admin who clicked the
   layers button once while testing will see every later change to the setting do nothing. Open the
   layers button on any map and choose **Site default**. The Maps section says so when it applies to
   the admin reading it.

7. The Maps section shows a key field on an install you never configured. You are running a build
   older than the fix that made "not set" mean OpenStreetMap. Older builds resolved an unset provider
   to CARTO, then fell back, so they asked for a key nobody had chosen. Update.

**Diagnostic**:

```bash
# Fetch a tile the way the browser does and look at the bytes.
# A refusal tile is small (~7 KB); a real one is much larger.
curl -s -o /tmp/tile.png -w '%{http_code} %{size_download} bytes\n' \
  'https://tile.openstreetmap.org/11/582/792.png'

# Same tile, identified. If this one is large and the first was small,
# the provider is refusing anonymous requests rather than failing.
curl -s -o /tmp/tile-ref.png -w '%{http_code} %{size_download} bytes\n' \
  -H 'Referer: https://your-site.example/' \
  'https://tile.openstreetmap.org/11/582/792.png'

# Then open both images. The refusal is legible.
```

In the browser, the active basemap and why it was chosen are visible without any tooling: open the
layers button on a map. The entry marked active is what you are looking at, and a line beneath the
list names the problem when the configured basemap could not be drawn.

See [Site Settings > Maps](configuration/site-settings.md#maps) for the full setting reference.

---

## Pokemon availability not showing

**Symptom**: The "Live > Spawning" filter doesn't appear in the Pokemon selector.

**Causes and fixes**:

1. Golbat not configured. Set `GOLBAT_API_ADDRESS` and `GOLBAT_API_SECRET` in `.env` and restart the container. The feature is only enabled when both are set.

2. Env vars not reaching the container. The compose file loads configuration via `env_file: .env`, so any var defined in `.env` is automatically passed in — no per-key entries in `docker-compose.yml` are needed. If you've customized your `docker-compose.yml` (a local copy of `docker-compose.yml.example`), confirm the `env_file: .env` line is still present under the app service.

3. Golbat API unreachable from container. Verify connectivity from inside the container. Check the app logs for `Failed to fetch available Pokemon from Golbat API` warnings.

4. Wrong API secret. The `GOLBAT_API_SECRET` must match Golbat's `api_secret` value in its `config.toml`. An incorrect secret results in `401 Unauthorised` responses.

5. Browser cache. Hard refresh (Ctrl+Shift+R) after deploying a new build. The old JavaScript bundle won't have the availability code.

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

1. Enable the feature on the bot. Set `quest_summary_enabled = true` under `[tracking]` in PoracleNG's `config.toml` and restart the processor.
2. Make sure the processor API is reachable. PoracleWeb.NET must be able to reach PoracleNG over HTTP. If they run on different machines or in separate containers, set `host = "0.0.0.0"` (or the LAN IP) under `[processor]` in PoracleNG's config — the `127.0.0.1` default refuses off-box connections.
3. Wait out the cache / hard refresh. The capability is cached for five minutes; reload the Quests page (Ctrl+Shift+R) after enabling.

!!! note
    A transient `503` from PoracleNG's summary endpoints is treated as a temporary backend fault, **not** as "feature off." The feature flag is read from the config endpoint, not inferred from a 503.

---

## Send summary now delivers nothing

**Problem**: Pressing **Send summary now** succeeds but no summary DM arrives.

**Solution**: Send summary now flushes only the quests PoracleNG has **buffered** since your last summary. An empty buffer delivers nothing — which is expected, not an error. To buffer quests:

1. **Enable Daily summary on at least one quest alarm** (the per-alarm toggle in the quest add/edit dialog). Only alarms with this toggle are buffered; the rest deliver immediately.
2. **Confirm the feature is enabled on the bot** (`tracking.quest_summary_enabled = true`) — when it is off, PoracleNG's matcher does not buffer at all, so the buffer stays empty.
3. Give it time. Quests are buffered as they match. Right after enabling the feature, or after a summary fires, the buffer starts empty and fills as matching quests come in. PoracleNG's status log shows the current count (`Summary: N buffered`).

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

1. `OIDC_END_SESSION_URL` configured. Without it, logout falls back to a plain local sign-out.
2. `enable_oidc_slo` not set to `false`. This admin runtime toggle defaults to on once the end-session URL is wired; an explicit `false` disables single logout.
3. The `post_logout_redirect_uri` registered at the IdP. PoracleWeb.NET sends `{origin}/login?loggedout=1`. If that URL isn't in the provider's allow-list, the provider rejects the logout redirect.

Configure the end-session URL, leave `enable_oidc_slo` unset (or `true`), and register the post-logout redirect URI at the IdP.

---

## Pokemon names and types stay English with the site set to another language

**Symptom**: The interface is translated, but the species picker still lists Bulbasaur, Blastoise and Butterfree, and the type chips read Bug, Dark, Dragon.

**Cause**: Those names come from Poracle, which translates them from its own bundle. If it cannot serve them, the site falls back to the English WatWowMap masterfile rather than showing nothing.

**Checks, in order**:

1. **Poracle is too old for the endpoint.** `GET /api/masterdata/monsters?locale=de` on your Poracle should answer with a map of `"{pokemonId}_{formId}"` entries. A 404 means the fallback is doing its job and only an upgrade will change it.

    ```bash
    curl -s -H "X-Poracle-Secret: $SECRET"       "http://poracle-host:3030/api/masterdata/monsters?locale=de" | head -c 200
    ```

2. **Poracle has no translation for that language.** It ships `de`, `en`, `es`, `fr`, `it`, `ja`, `nb-no`, `pl`, `ru`, `sv` and `zh-cn`. For `nl`, `pt`, `pt-BR` and `da` it returns English, and there is nothing to configure — the interface around the names is still translated.

3. **Poracle's game-data locale files are missing.** If names come back as their own keys (`poke_25` rather than `Pikachu`), Poracle's resource download has failed. The site ignores such values and keeps the English name, so the symptom is English names rather than visible keys. Check Poracle's startup log for its resource fetch.

4. **Move and item names are meant to be English.** Poracle serves no translated equivalent, so those pickers are unaffected by the display language.

**Note**: this follows the **display** language, not the alert language. The alert language decides what your DMs say. See [Internationalization](features/internationalization.md).

---

## An alarm type has disappeared and its admin toggle will not switch on

**Symptom**: A type is gone from the sidebar and the dashboard, its page redirects away, and on **Admin > Server settings** its toggle is off, greyed out and refuses to move.

**Cause**: Poracle has that type disabled in its own config. Its processor drops the webhook and its bot refuses the command, so alarms of that type could never fire — this site honours that rather than offering a feature the server will not deliver. Rules users already had are not deleted; they lie dormant and come back if the type is switched on again.

**Fix**: change it in Poracle's `config.toml`, not here. The flags are `disable_pokemon`, `disable_raid`, `disable_quest`, `disable_invasion`, `disable_lure`, `disable_nest`, `disable_gym`, `disable_max_battle`, `disable_fort_update` and `disable_showcase`. Restart Poracle afterwards; this site re-reads them within five minutes, or immediately on restart.

!!! warning "Pokéstop Events is read from a different place"
    You do not need to write `disable_showcase` into your `config.toml` to switch it on.
    `GET /api/config/values` reflects over the config Poracle actually loaded and reports the effective
    value, and the option is declared with a default of `false`, so a supported server answers `false`
    whether or not the line is present. Set it only when you want the feature **off**.

    Only an explicit `true` hides the page. A Poracle whose config this site cannot read, or one that
    answers without the option, leaves Pokéstop Events on, because every supported PoracleNG serves the
    `incident` route. If the page is missing, Poracle is reporting `disable_showcase = true`; check
    `GET /api/config/values` and the flag in `config.toml`.

    Unlike the others it does not travel in `disabledHooks`; it is read separately from
    `GET /api/config/values`, so the `jq .disabledHooks` check below will not show it.

**Diagnostic**:

```bash
# What Poracle reports as disabled
curl -s -H "X-Poracle-Secret: $SECRET"   http://poracle-host:3030/api/config/poracleWeb | jq .disabledHooks

# Pokéstop Events travels separately. `false` means available, `null` means
# the server did not report the option.
curl -s -H "X-Poracle-Secret: $SECRET"   http://poracle-host:3030/api/config/values | jq .values.general.disable_showcase

# What this site resolved that into (any signed-in user)
curl -s -H "Authorization: Bearer $JWT"   https://your-site/api/settings/upstream-disabled
```

An empty array on the first call, with the type still locked, means the local `disable_*` site setting is the one doing it — that one is yours to change on **Admin > Server settings**. The page names which side it came from, so you should not have to run either command to find out.

---

## A feature is missing and nothing says why

**Symptom**: A page or control the docs mention isn't there, with no error, toast or empty state.

**Cause**: Every supported PoracleNG (5.4.0 and later) has the quiet chip, Pokécoin quest rewards, the
costume filter and the button that moves a saved place, so PoracleWeb.NET always offers them. What can
hide something without a word is configuration:

| Missing | Hidden by |
|---|---|
| An alarm type's page and sidebar item | Its `disable_*` site setting on **Admin > Server settings**, or the type in Poracle's own `disabledHooks` (see the section above) |
| Pokéstop Events | Poracle's `general.disable_showcase = true`, or the `disable_showcase` site setting |
| Quest summary delivery | Poracle's `tracking.quest_summary_enabled` not `true` |
| The Pokemon edit-in-place toggle | Poracle's `tracking.pokemon_edit` not `true` |
| Areas & places, Profiles, Geofences and similar pages | Their `disable_*` site settings |

The two `tracking.*` values are read from `GET /api/config/values` and cached for five minutes, and
read as off when PoracleNG can't be asked.

**Fix**: check the setting on **Admin > Server settings**, then Poracle's `config.toml`. The Versions card on
the same page shows which PoracleNG the deployment is talking to; below 5.4.0 these features fail with a
message naming the version rather than hiding.

`PoracleUnsupportedException` is what a request gets when it reaches a route the server doesn't have: a
human, profile, area, place or geocode call on PoracleNG below 5.4.0 answers **409 Conflict** naming what
is missing. When PoracleNG can't be reached at all, the mute and location endpoints and every alarm endpoint answer **503**.

```json
{
  "error": "This PoracleNG does not support listing areas. It requires PoracleNG 5.4.0.",
  "feature": "listing areas",
  "requires": "PoracleNG 5.4.0"
}
```

409 and not 403 on purpose: 403 is what a `disable_*` site setting answers, which is an operator's
switch and a different conversation. A 409 carries the one detail worth reading, so the SPA shows it
beside the control rather than bouncing you to the dashboard.

---

## A delegate cannot see "My Destinations"

**Symptom**: You granted someone a destination on **Admin > Destinations**, and they report no *My
Destinations* item in the sidebar.

**Checks, in order**:

1. **Did the grant land?** Reopen the destination's delegates dialog. A removable chip with their name
   means it exists. A grant naming an account that does not exist is refused rather than stored, so an
   absent chip usually means they had never signed in when you added them — they must register with
   the Poracle bot and sign in once before they can be granted.

2. **Give it a minute.** Delegation is resolved live but cached for about a minute per person.

3. **Are they an administrator?** Admins can manage any destination and are never listed as a delegate
   of a particular one, so they never see the item. It is for delegates only.

4. **Are you thinking of the bot?** `discord.webhook_admins` in Poracle's config grants access through
   the Discord bot, not here. The reverse also holds: a row added here grants nothing to the bot. A
   Poracle-side grant *does* work here, but not the other way round.

5. **Expecting a PoracleNG channel admin?** Someone PoracleNG lists in `[[discord.delegated_admins]]` or
   `[[telegram.delegated_admins]]` appears only when **Admin → Settings → Administration → Let
   Poracle's channel admins manage here** (`delegation_poracle_channels`) is on, and only for channels registered as
   destinations on this site. It is off by default.

**Note**: before v2.17 the item came from a claim stamped into the sign-in token, so a new delegate
waited up to 24 hours or had to sign out and back in. If you are running an older build, that is the
explanation — see [Destinations & Delegates](features/webhooks.md).

---

## Telegram and linked accounts

Setup is in [Telegram](configuration/telegram.md) and behaviour in [Linked accounts](features/linked-accounts.md).

### The Telegram button doesn't appear

**Symptom**: No Telegram button on the login page, or none on the Telegram card of Linked accounts.

**Cause**: The button is drawn only when `TELEGRAM_ENABLED=true`, a bot username is known (`TELEGRAM_BOT_USERNAME`,
or the `telegram_bot` setting when that's empty), and **Enable Telegram login** isn't switched off. A button that
appears but says "Bot domain invalid" means the bot's domain in @BotFather isn't this site's.

**Fix**: Set the three values and restart after `.env` changes. In @BotFather, `/setdomain` with the exact hostname
people use. The widget works on one domain only.

### Telegram says the account isn't registered

**Symptom**: Telegram sign-in answers "Your account is not registered", or a linked Telegram card reads
"Waiting to register".

**Cause**: PoracleNG has no `telegram:user` for that id. It registers only members of a registration group
(`[telegram] channels`, or each community's `telegram.channels` with area security).

**Fix**: Join the registration group and send `/poracle` there. Set `telegram_registration_url` so the Linked
accounts page shows a **Join the Telegram group** button. Or switch on `link_register_missing` so linking creates
the account.

### /start does nothing

**Cause**: `register_on_start = true` and the person isn't in any registration group. PoracleNG doesn't reply in that
case (jfberry/PoracleNG#335). With `register_on_start` off it answers "You are not registered. Please register first."

**Fix**: Join the group and send `/poracle` there.

### Registered on Telegram but no alerts arrive

**Cause**: Telegram lets a bot DM only someone who has started a chat with it.

**Fix**: Press **Start** in the bot's chat. Allowing the bot to message you in the site's login widget also counts.

### Discord and Telegram show different settings

**Cause**: A bot command (`!location`, `/profile`, `/area add`) changed one account only, or a save reached one app
and not the other.

**Fix**: Open Linked accounts and pick **Use Discord's** or **Use Telegram's** on each line. The site never picks for
the person.

### A difference won't go away

**Checks**:

1. **Profile 0.** An older account may have a profile 0, which the other app can't have. It's left out of the
   comparison on purpose, and the page says which account has one. That note isn't a difference to fix.
2. **"That changed in the meantime".** Something changed one app while the list was open. Choose again from the
   refreshed list.
3. **A profile or place in use.** A profile that still holds alarms on the other app, or a place alarms there still
   use, isn't deleted. Move or change those alarms, or keep that app's side instead.
4. **Active profile.** It can only be matched once the other app has that profile. Fix the Profiles line first.

### An alarm can't be moved to the other app

| Message | Fix |
|---|---|
| Discord and Telegram are on different profiles | Match the active profile on Linked accounts, then move again |
| The place isn't saved on the other app | Match saved places, or save that place on the other app |
| It's limited to an area the other app can't use | Change the alarm's areas |
| Alerts can't be moved while inspecting another account | Admins viewing as someone can't move their alarms. The person has to do it |
| "…but the Discord copy is still there" | The move worked and the delete didn't. Delete the leftover copy from the list |

### People are never removed after leaving the group

**Cause**: `[general] role_check_mode` is `ignore`, PoracleNG's default, or `[telegram] check_role` is off. The
check never creates anyone, only disables or deletes.

**Fix**: Set `role_check_mode = "disable-user"` (or `"delete"`) and `check_role = true`. See
[Removing people who leave](configuration/telegram.md#removing-people-who-leave).
