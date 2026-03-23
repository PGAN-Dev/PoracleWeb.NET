# PR #35 Code Review Report

## Executive Summary

| Metric | Rating |
|---|---|
| **Overall Grade** | B+ |
| **Code Quality** | Good — clean architecture, proper layering, some data integrity gaps |
| **Requirements Satisfied** | Full — all #34 requirements implemented |
| **Architecture Fit** | Strong — consistent with existing PoracleWeb patterns |
| **Risk Level** | Medium — 2 data integrity bugs need fixing before merge |

## PR Details

- **Title**: Migrate pweb_settings to structured tables in PoracleWeb DB
- **Branch**: `feature/structured-settings-tables` → `main`
- **Author**: hokiepokedad2
- **Created**: 2026-03-23
- **Commits**: 7
- **Changes**: +3,236 / -313 across 55 files
- **Closes**: #34

---

## Critical Issues (Must Fix)

| # | File:Line | Issue | Impact |
|---|---|---|---|
| 1 | `QuickPickAppliedStateRepository.cs:80-90` | **`MapToModel` does not set `UserId` or `ProfileNo`** — when an applied state is read from DB and re-saved (e.g., during UID cleanup at QuickPickService.cs:86-87), the state is persisted with `UserId=""` and `ProfileNo=0`, corrupting the record | **Data corruption** on quick pick UID cleanup |
| 2 | `SettingsController.cs:14-19` | **`GetAll` endpoint has no admin check** — any authenticated user can read all site settings including sensitive values (api_secret, telegram_bot_token) | **Security** — sensitive data exposure |
| 3 | `SettingsController.cs:38-44` | **`ValueType` never set from API** — `Upsert` creates `SiteSetting` with only Key/Value/Category but ValueType defaults to `"string"`, silently overwriting the type metadata set during migration | **Data integrity** — type metadata lost on admin updates |
| 4 | `WebhookDelegateRepository.cs:40-52` | **`AddAsync` has no duplicate check** — the unique index on (webhook_id, user_id) throws `DbUpdateException` on duplicates, resulting in unhandled 500 errors | **Runtime error** — 500 on duplicate delegate add |

## Major Issues (Should Fix)

| # | File:Line | Issue | Recommendation |
|---|---|---|---|
| 5 | Migration `InitialPoracleWeb.cs` | **Missing index `IX_webhook_delegates_user_id`** — `GetWebhookIdsByUserIdAsync` queries by `user_id` alone but the composite index `(webhook_id, user_id)` doesn't cover this; will table scan | Add single-column index on `user_id` |
| 6 | Migration `InitialPoracleWeb.cs` | **Missing index `IX_quick_pick_definitions_scope_owner`** — `GetAllGlobalAsync` and `GetByOwnerAsync` filter by `scope`/`owner_user_id` with no covering index | Add composite index `(scope, owner_user_id)` |
| 7 | `SettingsMigrationService.cs:133-209` | **No transaction wrapping** — partial failure leaves inconsistent state; retry creates duplicate webhook delegates | Wrap in transaction or make operations idempotent |
| 8 | `SiteSettingService.cs:29-33` | **`GetPublicAsync` loads all settings then filters in memory** — public endpoint should filter at DB level | Add `GetByKeysAsync` to repository or query with WHERE clause |
| 9 | `QuickPickService.cs:200` | **Silent fallback to "monster" when definition deleted** — `RemoveAsync` uses `definition?.AlarmType ?? "monster"` so if the definition was "raid", tracked alarm rows are deleted from wrong table, leaving orphans | Throw or store alarm type in applied state |
| 10 | `settings.service.ts:71-76` | **`update()` never sends `category`** — admin UI updates null out the category field on save | Include category in the PUT body |
| 11 | `ServiceCollectionExtensions.cs:75` | **`SettingsMigrationService` registered as concrete type** not interface | Register as `ISettingsMigrationService` for testability |

## Minor Issues (Consider)

| # | File:Line | Issue | Suggestion |
|---|---|---|---|
| 12 | `SettingsController.cs:54-77` | `SiteSettingRequest.Setting` property documented as "legacy compat" but never read | Remove dead property or implement fallback logic |
| 13 | `MariaDbHistoryRepository.cs:41-67` | `GET_LOCK` return value not checked — silently proceeds if lock fails | Check return value, log warning if != 1 |
| 14 | `QuickPickService.cs:152-154` | `trackedUids` initialized then immediately reassigned | Remove redundant initialization |
| 15 | `PoracleMappingProfile.cs:94-99` | AutoMapper Filters mapping duplicates `QuickPickDefinitionRepository.MapToModel` logic | Remove one; prefer repository's manual mapping |
| 16 | `QuickPickService.cs:394-426` | Non-monster alarm types deserialize filters without whitelist protection | Add filter sanitization for raid/quest/egg types |
| 17 | `admin-settings.component.html:33` | Settings with `null` values hidden from admin UI | Use `!== undefined` instead of `!== null` |
| 18 | Interface return types | Mixed `List<T>` vs `IEnumerable<T>` and `Task` vs `Task<T>` across new repositories | Standardize on `IEnumerable<T>` and `Task<T>` for consistency |

## Test Coverage Assessment

| Test File | Coverage | Gaps |
|---|---|---|
| `SiteSettingServiceTests.cs` | Good (15 tests) | Missing: `DeleteAsync` false path, `GetBoolAsync("")` edge case |
| `WebhookDelegateServiceTests.cs` | Good (6 tests) | Missing: empty results, duplicate add error path |
| `SettingsMigrationServiceTests.cs` | **Partial** (6 tests) | **Missing**: `user_quick_pick:` path, `qp_applied:` path, category/valueType mapping, DetermineValueType logic |
| `SettingsControllerTests.cs` | **Thin** (3 tests) | **Missing**: `GetPublic` endpoint (anonymous), request body mapping |
| `AdminControllerTests.cs` | Good (30+ tests) | Missing: `GetWebhookDelegates` single-webhook query |
| `QuickPickServiceSecurityTests.cs` | Targeted | Does not assert injected `Id` field was stripped |
| Frontend `settings.service.spec.ts` | Excellent (30 tests) | No gaps |

## Risk Assessment

| Risk | Level | Notes |
|---|---|---|
| Data Integrity | **Medium** | `MapToModel` missing UserId/ProfileNo (#1), ValueType overwrite (#3) |
| Security | **Medium** | `GetAll` exposes all settings to non-admins (#2) |
| Performance | **Low** | Missing indexes (#5, #6) — low row counts mitigate |
| Breaking Change | **Low** | Frontend handles both old/new API shapes |
| Rollback Complexity | **Low** | Old `pweb_settings` table untouched; can revert by re-registering old services |

---

## Final Verdict: **APPROVED WITH CONDITIONS**

### Pre-Merge Requirements
- [ ] Fix `QuickPickAppliedStateRepository.MapToModel` to include `UserId` and `ProfileNo` (#1)
- [ ] Add admin check to `SettingsController.GetAll` or filter sensitive settings for non-admins (#2)
- [ ] Preserve `ValueType` in `SettingsController.Upsert` (#3)
- [ ] Handle duplicate delegate in `WebhookDelegateRepository.AddAsync` (#4)

### Post-Merge Recommendations
- [ ] Add missing database indexes via new migration (#5, #6)
- [ ] Add tests for `user_quick_pick:` and `qp_applied:` migration paths
- [ ] Add `GetPublic` endpoint test
- [ ] Wrap migration in transaction or make idempotent (#7)
- [ ] Store `alarmType` in `QuickPickAppliedState` to avoid silent fallback (#9)

### Rationale
The architecture is sound — proper separation of concerns, clean interfaces, and a well-designed migration strategy. The EF Core migrations with MariaDB compatibility fix is a solid improvement. The 4 critical issues are all fixable without architectural changes. The PR delivers significant performance improvements (eliminating full-table scans) and moves application data out of the Poracle DB as intended. With the pre-merge fixes applied, this is ready to ship.
