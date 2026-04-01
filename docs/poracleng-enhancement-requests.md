# PoracleNG API Enhancement Requests

This document tracks PoracleNG API gaps that require workarounds in PoracleWeb. Each gap is referenced by inline `HACK`/`TODO` comments throughout the codebase.

## Background

PoracleWeb now proxies all alarm tracking writes through the PoracleNG REST API (see [PoracleNG API Proxy](architecture/poracleng-proxy.md)). This migration was prompted by a March 31, 2026 incident where a NULL `template` column written directly by PoracleWeb crashed PoracleNG's state reload for 15 hours.

The migration is complete for all alarm CRUD operations. However, some operations lack dedicated PoracleNG endpoints and use fetch-modify-repost workarounds that are less efficient. The gaps listed below are these operations.

---

## Gaps

### Bulk Distance Update

**ID:** `bulk-distance-update`  
**Priority:** High  
**Code refs:** Each alarm service's `UpdateDistanceAsync` and `UpdateDistanceBulkAsync` methods

**Current behavior:** PoracleWeb fetches all alarms of a type via the proxy, modifies the `distance` field in memory, and POSTs them back. Two variants:
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
**Code refs:** `CleaningService.cs`

**Current behavior:** PoracleWeb fetches all alarms of a type via the proxy, sets the `clean` field (0 or 1), and POSTs them back. Used for the "auto-clean" feature that deletes alarms after they fire.

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

**Current behavior:** `DashboardService` calls `IPoracleTrackingProxy.GetAllTrackingAsync()` which returns full alarm payloads for all types. Counts are extracted from the response arrays.

**What's needed:** A PoracleNG endpoint that returns alarm counts per type:
```
GET /api/tracking/counts/{id}
Response: { "pokemon": 537, "raid": 27, "egg": 17, "quest": 38, ... }
```

**Workaround without enhancement:** Single `GET /api/tracking/all/{id}` call returns full payloads for all types. Works but returns complete alarm objects just to count them.

---

### Admin Delete All Alarms

**ID:** `admin-delete-all-alarms`  
**Priority:** Medium  
**Code refs:** `HumanService.cs:DeleteAllAlarmsByUserAsync`

**Current behavior:** PoracleWeb fetches all UIDs per alarm type via the proxy, then bulk-deletes each type sequentially.

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
**Priority:** Low (adopted)  
**Code refs:** `ProfileController.cs:SwitchProfile`, `PoracleHumanProxy.cs:SwitchProfileAsync`

**Status: Adopted.** `ProfileController.SwitchProfile` now calls `IPoracleHumanProxy.SwitchProfileAsync(userId, profileNo)` -- a single atomic call. PoracleNG handles saving the old profile's areas and loading the new profile's areas internally. The multi-step dual-write has been removed.

---

### Atomic Area Update

**ID:** `atomic-area-update`  
**Priority:** Low (adopted)  
**Code refs:** `AreaController.cs:UpdateAreas`, `PoracleHumanProxy.cs:SetAreasAsync`

**Status: Adopted.** `AreaController.UpdateAreas` now calls `IPoracleHumanProxy.SetAreasAsync(userId, areas)` -- a single atomic call. PoracleNG handles the dual-write to both `humans.area` and `profiles.area` internally. `UserGeofenceService` also uses `SetAreasAsync` for geofence activate/deactivate operations on the active profile.

---

### NULL Field Defaults

**ID:** `null-field-defaults`  
**Priority:** Low (resolved)

**Status: Resolved.** `BaseRepository` and `EnsureNotNullDefaults()` have been removed. All alarm writes go through the PoracleNG API, which applies proper defaults via `cleanRow()`. Remaining direct-DB repositories (`HumanRepository` for admin ops, `poracle_web`-owned tables) handle null normalization as needed.

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

| Gap | Priority | Workaround in Use | Status |
|-----|----------|-------------------|--------|
| Bulk distance update | High | Fetch all, modify, POST back | Working but inefficient |
| Bulk clean toggle | High | Fetch all, modify, POST back | Working but inefficient |
| monsters.go COALESCE | High | PoracleNG bug -- needs fix upstream | PoracleNG fix needed |
| Dashboard counts | Medium | Single GET /api/tracking/all call | Working (returns full payloads) |
| Admin delete all alarms | Medium | Fetch UIDs per type, bulk delete each | Working |
| Profile delete cascade | Medium | Unknown | Need verification |
| Atomic profile switch | Low | Already in PoracleNG | **Adopted** |
| Atomic area update | Low | Already in PoracleNG | **Adopted** |
| NULL field defaults | Low | Handled by PoracleNG cleanRow() | Resolved by migration |
