# Troubleshooting

## PoracleNG unreachable (alarm operations fail)

**Problem**: All alarm operations (create, edit, delete, list) fail with HTTP 500 errors. The dashboard shows zero alarms. Logs show `HttpRequestException` or `TaskCanceledException` when calling the PoracleNG API.

**Solution**: Check that PoracleNG is running and reachable from the PoracleWeb container:

1. Verify `Poracle:ApiAddress` points to the correct PoracleNG host and port
2. If using Docker, ensure both containers are on the same network or the PoracleNG port is exposed
3. Test connectivity: `curl http://<poracle-api-address>/api/config/poracleWeb` from inside the PoracleWeb container
4. Verify `Poracle:ApiSecret` matches PoracleNG's `server.apiSecret` config value

!!! danger "All operations go through PoracleNG"
    Unlike previous versions where PoracleWeb wrote directly to MySQL, all alarm tracking, user registration, location setting, area management, and profile switching now require a running PoracleNG instance. If PoracleNG is down, users cannot create/edit/delete/view alarms, register, set their location, update areas, or switch profiles. Only admin bulk operations (list all users, delete user) use direct DB access.

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

**Solution**: PoracleNG returns alarm data in snake_case JSON (`pokemon_id`, `min_iv`, `max_cp`). PoracleWeb deserializes this with `JsonNamingPolicy.SnakeCaseLower`. If a field is not deserializing correctly:

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

**Solution**: Use `discordapp.com` (not `discord.com`) for API calls. The `discord.com` domain is blocked by Cloudflare in some server environments. PoracleWeb is already configured to use `discordapp.com`.

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

**Solution**: Koji's `displayInMatches` custom property is not reliably honored by all Poracle format serializers. Serve user geofences from the PoracleWeb feed endpoint (`/api/geofence-feed`) instead of pushing them to Koji. Only promote to Koji when an admin approves a geofence for public use.

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

!!! note "Legacy issue"
    This was caused by direct database writes with incorrect C# model defaults. New alarms created through the PoracleNG API proxy have correct defaults. The SQL queries below help diagnose alarms created before the migration.

**Problem**: New monster alarms created via the web UI may silently filter out pokemon if model defaults don't match PoracleJS expectations. For example, `max_size=0` causes all pokemon with size data to be rejected, and `size=0` instead of `size=-1` shows incorrectly in the old PHP UI as "-XXL".

**Solution**: All Create model defaults are aligned with the PHP PoracleWeb `include/defaults.php`. Key values:

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

## Pokemon availability not showing

**Symptom**: The "Live > Spawning" filter doesn't appear in the Pokemon selector.

**Causes and fixes**:

1. **Golbat not configured**: Set `GOLBAT_API_ADDRESS` and `GOLBAT_API_SECRET` in `.env` and restart the container. The feature is only enabled when both are set.

2. **docker-compose.yml missing Golbat vars**: Ensure your `docker-compose.yml` passes the Golbat env vars to the container:
   ```yaml
   environment:
     - Golbat__ApiAddress=${GOLBAT_API_ADDRESS:-}
     - Golbat__ApiSecret=${GOLBAT_API_SECRET:-}
   ```

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
