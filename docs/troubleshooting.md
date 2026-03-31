# Troubleshooting

## MySQL provider incompatibility

**Problem**: Build errors or runtime exceptions related to `Pomelo.EntityFrameworkCore.MySql`.

**Solution**: This project uses `MySql.EntityFrameworkCore` (Oracle's official provider), **not** Pomelo. Pomelo is incompatible with EF Core 10. Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

---

## NULL string constraint violations

**Problem**: MySQL errors like `Column 'X' cannot be null` when saving entities.

**Solution**: Call `EnsureNotNullDefaults()` before saving. Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. The method sets null strings to `""`.

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

**Solution**: Never send partial objects to `PUT /{uid}`. AutoMapper maps all fields — `int` properties default to `0` when absent from JSON. Use the dedicated `PUT /distance/bulk` endpoint for distance changes, or spread the full alarm object:

```typescript
// ✅ Correct
this.http.put(`/api/pokemon/${uid}`, { ...alarm, distance });

// ❌ Wrong
this.http.put(`/api/pokemon/${uid}`, { distance });
```

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

**Solution**: `MySql.EntityFrameworkCore` may store null `string?` values as empty strings due to the `EnsureNotNullDefaults()` normalization in `BaseRepository`. Poracle treats `gym_id = ''` as "track a specific gym with an empty ID," which matches nothing. The fix is nullable string normalization that preserves `NULL` for gym_id columns.

To fix existing data:

```sql
UPDATE gym SET gym_id = NULL WHERE gym_id = '';
UPDATE egg SET gym_id = NULL WHERE gym_id = '';
UPDATE raid SET gym_id = NULL WHERE gym_id = '';
```

---

## GymCreate.Team defaults to 0 (Neutral only)

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

## Monster filter defaults (size, max_level, etc.)

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
