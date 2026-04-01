# PoracleNG API Enhancement Requests

This document tracks PoracleNG API gaps that block PoracleWeb from migrating off direct Poracle database writes. Each gap is referenced by inline `HACK`/`TODO` comments throughout the codebase.

## Background

PoracleWeb currently writes directly to the Poracle MySQL database (humans, profiles, and all alarm tables). This bypasses PoracleNG's data validation, default handling, and state reload mechanism. On March 31, 2026, a NULL `template` column written by PoracleWeb crashed PoracleNG's state reload for 15 hours, causing stale alarm state and unwanted DM floods.

PoracleNG exposes a comprehensive REST API (`/api/tracking/*`, `/api/humans/*`, `/api/profiles/*`) that handles field defaults, dedup, and triggers immediate state reload on every mutation. Migrating PoracleWeb to proxy writes through this API would eliminate an entire class of data integrity bugs.

The gaps listed below are operations PoracleWeb performs that have **no direct PoracleNG API equivalent**.

---

## Gaps

### Bulk Distance Update

**ID:** `bulk-distance-update`  
**Priority:** High  
**Code refs:** `BaseRepository.cs:UpdateDistanceByUserAsync`, `BaseRepository.cs:UpdateDistanceByUidsAsync`

**Current behavior:** PoracleWeb loads all alarm entities into memory, sets the `distance` field via reflection, and calls `SaveChangesAsync()`. Two variants:
1. Update ALL alarms of a type for a user/profile
2. Update specific alarms by UID list

**What's needed:** A PoracleNG endpoint that accepts a batch distance update:
```
PUT /api/tracking/{type}/{id}/distance
Body: { "distance": 500 }
// Updates all alarms of {type} for user {id} on their active profile

PUT /api/tracking/{type}/{id}/distance/bulk
Body: { "uids": [1, 2, 3], "distance": 500 }
// Updates specific alarm UIDs
```

**Workaround without enhancement:** Fetch all alarms via GET, then POST each one back with the distance field changed. Very inefficient for users with hundreds of alarms.

---

### Bulk Clean Toggle

**ID:** `bulk-clean-toggle`  
**Priority:** High  
**Code refs:** `CleaningService.cs`, `BaseRepository.cs:BulkUpdateCleanAsync`

**Current behavior:** PoracleWeb loads all alarm entities of a type for a user/profile, sets the `clean` field (0 or 1), and calls `SaveChangesAsync()`. Used for the "auto-clean" feature that deletes alarms after they fire.

**What's needed:** A PoracleNG endpoint to batch-toggle the clean flag:
```
PUT /api/tracking/{type}/{id}/clean
Body: { "clean": 1 }
// Sets clean=1 on all alarms of {type} for user {id} on their active profile
```

**Workaround without enhancement:** Fetch all alarms via GET, then POST each back with the clean field changed. Same inefficiency as bulk distance.

---

### Dashboard Counts

**ID:** `dashboard-counts`  
**Priority:** Medium  
**Code refs:** `DashboardService.cs`

**Current behavior:** PoracleWeb runs `SELECT COUNT(*)` on each of 8 alarm tables sequentially to populate the dashboard summary.

**What's needed:** A PoracleNG endpoint that returns alarm counts per type:
```
GET /api/tracking/counts/{id}
Response: { "pokemon": 537, "raid": 27, "egg": 17, "quest": 38, ... }
```

**Workaround without enhancement:** Call `GET /api/tracking/{type}/{id}` for each of 8 types and count the response arrays client-side. Works but makes 8 HTTP calls instead of 1.

---

### Admin Delete All Alarms

**ID:** `admin-delete-all-alarms`  
**Priority:** Medium  
**Code refs:** `HumanService.cs:DeleteAllAlarmsByUserAsync`

**Current behavior:** PoracleWeb runs `ExecuteDeleteAsync` on each of 8 alarm tables sequentially to wipe a user's tracking data (admin action).

**What's needed:** A PoracleNG admin endpoint:
```
DELETE /api/tracking/all/{id}
// Deletes ALL tracking for user {id} across all alarm types and profiles
```

**Workaround without enhancement:** Loop through each alarm type, GET all UIDs, then POST bulk delete for each. Slow and not atomic.

---

### Profile Delete Cascade

**ID:** `profile-delete-cascade`  
**Priority:** Medium  
**Code refs:** `ProfileController.cs:Delete`

**Current behavior:** PoracleWeb only deletes the `profiles` row. It does NOT:
- Delete alarm records scoped to that profile (`monsters`, `raid`, `egg`, etc. with matching `profile_no`)
- Reassign `humans.current_profile_no` if the active profile is deleted
- Remove the profile's areas from `humans.area`

**What's needed:** Confirm that PoracleNG's `DELETE /api/profiles/{id}/byProfileNo/{n}` cascades:
1. Deletes all alarm rows with matching `(id, profile_no)`
2. Reassigns `humans.current_profile_no` to profile 1 (or another valid profile) if the active profile is deleted
3. Updates `humans.area` if the deleted profile was active

If PoracleNG already handles this, PoracleWeb can simply proxy the call.

---

### Atomic Profile Switch

**ID:** `atomic-profile-switch`  
**Priority:** Low (already available in PoracleNG)  
**Code refs:** `ProfileController.cs:SwitchProfile`

**Current behavior:** PoracleWeb performs two separate `SaveChangesAsync()` calls (save old profile's areas, then load new profile's areas into `humans`). Not transactional — if the second write fails, `humans.area` and `profiles.area` become inconsistent.

**PoracleNG status:** `POST /api/humans/{id}/switchProfile/{profile}` likely handles this atomically. **Need to verify** that it performs the same area save/load dual-write.

**Migration path:** Replace the multi-step controller logic with a single PoracleNG API call. Verify response includes updated profile data for JWT reissuance.

---

### Atomic Area Update

**ID:** `atomic-area-update`  
**Priority:** Low (already available in PoracleNG)  
**Code refs:** `AreaController.cs:UpdateAreas`

**Current behavior:** PoracleWeb dual-writes to `humans.area` and `profiles.area` with two separate `SaveChangesAsync()` calls. Not transactional.

**PoracleNG status:** `POST /api/humans/{id}/setAreas` likely handles this atomically. **Need to verify** it writes to both `humans.area` and `profiles.area`.

**Migration path:** Replace dual-write with single PoracleNG API call.

---

### NULL Field Defaults

**ID:** `null-field-defaults`  
**Priority:** Low (already handled by PoracleNG)  
**Code refs:** `BaseRepository.cs:EnsureNotNullDefaults`

**Current behavior:** PoracleWeb uses reflection-based `EnsureNotNullDefaults()` to coerce NULL strings to empty strings before writing to the DB. This is needed because the Poracle DB has `NOT NULL` text columns that EF Core maps as `string?`.

**PoracleNG status:** PoracleNG's `cleanRow()` function in each tracking handler applies proper defaults for ALL fields. Template defaults to config's `defaultTemplateName` (fallback "1"), Ping defaults to "", all numeric fields have explicit defaults.

**Migration path:** Once writes go through PoracleNG API, `EnsureNotNullDefaults` can be removed entirely.

---

### PoracleNG monsters.go COALESCE Gap

**ID:** `monsters-go-coalesce`  
**Priority:** High (bug in PoracleNG)  
**Not a PoracleWeb code ref — this is a PoracleNG bug**

**Issue:** In `/source/PoracleNG/processor/internal/db/monsters.go`, the SQL query selects `template` and `ping` as raw columns without `COALESCE`:
```sql
template, clean, ping
```

Every other tracking query file (quests.go, invasions.go, raids.go, gyms.go, lures.go, nests.go, forts.go, tracking_queries.go) correctly uses:
```sql
COALESCE(template, '1') AS template
```

When `template IS NULL` in the database, Go's `database/sql` scanner crashes with:
```
sql: Scan error on column index 25, name "template": converting NULL to string is unsupported
```

This crashes the **entire state reload**, freezing PoracleNG on stale data for all users until restarted.

**Fix:** Add `COALESCE(template, '1') AS template, clean, COALESCE(ping, '') AS ping` to the monsters query in `monsters.go`, matching all other tracking files.

---

## Summary Table

| Gap | Priority | Workaround Available | Blocks Migration |
|-----|----------|---------------------|-----------------|
| Bulk distance update | High | Yes (inefficient) | Partial |
| Bulk clean toggle | High | Yes (inefficient) | Partial |
| monsters.go COALESCE | High | PoracleWeb defaults template to "" | Yes (bug) |
| Dashboard counts | Medium | Yes (8 API calls) | No |
| Admin delete all alarms | Medium | Yes (loop + bulk delete) | No |
| Profile delete cascade | Medium | Unknown | Need verification |
| Atomic profile switch | Low | Already in PoracleNG | No |
| Atomic area update | Low | Already in PoracleNG | No |
| NULL field defaults | Low | Already in PoracleNG | No |
