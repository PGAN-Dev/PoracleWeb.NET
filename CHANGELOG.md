# Changelog

All notable changes to PoracleWeb are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]


### Fixed
- **Raid card team icon**: `team=4` (all teams) tried to load nonexistent `gym/4.png`, now maps to gray gym icon ([#66](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/66), [PR #67](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/67))
- **Raid/egg "Any Team" selector**: sent `team=0` (uncontested gym) instead of `team=4` (all teams), silencing raid notifications for affected users ([#63](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/63), [PR #64](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/64))
- remove decorative circle from page headers ([PR #62](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/62))
- DTS preview renders nested Handlebars if-blocks incorrectly ([PR #60](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/60))
## [1.0.2] - 2026-03-24

### Fixed
- PVP ranking fields (`pvp_ranking_best`, `pvp_ranking_worst`) sent with non-default values when no PVP league selected, causing Poracle to use wrong DTS template branch for all Pokemon alerts ([#57](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/57), [PR #58](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/58))

## [1.0.1] - 2026-03-24

### Added
- **Admin geofence submissions page redesign**: Region grouping, three view modes (card/list/table), sortable columns, Discord avatar display, resolved reviewer names ([#55](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/55), [PR #56](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/56))
- **Reviewer name resolution**: Backend resolves `reviewedBy` Discord IDs to display names and avatars via batch human lookup
- **Table view**: Flat ungrouped table with sortable columns (name, status, owner, region, points, created, submitted) and reviewer info
- **Region grouping**: Card and list views group geofences by region with collapsible expansion panels

### Fixed
- Map thumbnails lost when switching between card and list views — maps now properly destroyed and re-initialized on view switch

## [1.0.0] - 2026-03-24

### Added
- **PoracleNG compatibility**: officially supports both PoracleJS and PoracleNG
- **GitHub Pages docs**: pymdownx.emoji extension for Material icon rendering in Quick Links ([#53](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/53), [PR #54](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/54))

## [0.6.4] - 2026-03-24

### Added
- **Raid/Egg alarm fields**: move, evolution, exclusive, gym_id, rsvp_changes ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))
- **Gym alarm fields**: battle_changes toggle and gym_id ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))
- **Invasion**: grunt_type automatically lowercased on create for Poracle case-sensitive matching ([PR #52](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/52))

### Fixed
- Size dropdown "Any" option sent incorrect values (0 instead of -1/5), silently breaking size filters ([#49](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/49), [PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- Model defaults aligned with PHP PoracleWeb: size=-1, max_level=55, raid/egg team=4, raid move/evolution=9000 ([PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- Frontend dialog fallbacks and validators aligned with PHP defaults ([PR #50](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/50))
- pvpRankingWorst edit dialog fallback corrected from 100 to 4096
- Level input max=50 corrected to 55 in edit dialog
- Site settings update fails with EF key modification error when toggling settings

## [0.6.3] - 2026-03-24

### Fixed
- set correct defaults on MonsterCreate model to prevent silent filter rejection ([PR #48](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/48))

## [0.6.2] - 2026-03-24

### Added
- add size filter to Pokemon alarm dialogs ([PR #43](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/43))
- **Admin geofence detail view**: enriched admin geofence management page with owner display names, avatar URLs, interactive Leaflet map detail dialog, lazy-loaded map thumbnails, point count, area calculation (m²/km²), reference geofences from Poracle areas, and skeleton loading ([#42](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/42), [PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **Site settings documentation**: comprehensive GitHub Pages docs covering all 39+ admin-configurable settings organized by category with types, descriptions, and prerequisites ([#44](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/44), [PR #45](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/45))

### Changed
- extract `GetDefaultAvatarUrl` to shared `AvatarCacheService.GetAvatarOrDefault()` static method, removing duplication from `AdminController` and `AdminGeofenceController` ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- extract `GEOFENCE_STATUS_COLORS` to shared `geofence.utils.ts` constant ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))

### Fixed
- **Internal settings visible in admin UI**: hide `migration_completed` and other internal system settings from the admin settings API and block writes with `BadRequest`. Frontend defense-in-depth filtering added as well. ([#44](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/44), [PR #45](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/45))
- **Map thumbnails cleared on tab switch**: admin geofence cards now properly destroy orphaned Leaflet maps when filtered out and re-initialize them when switching back ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **Leaflet map not rendering in detail dialog**: use `dialogRef.afterOpened()` and `height !important` to prevent Material dialog animation and Leaflet global CSS from collapsing the map container ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))
- **MySql.EntityFrameworkCore `Contains()` query failure**: replace `List<T>.Contains()` LINQ with sequential lookups for batch human ID resolution ([PR #46](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/46))

## [0.6.1] - 2026-03-23

### Added
- **Per-profile geofence toggle**: Slide toggle on each custom geofence card to activate/deactivate notifications per profile without recreating the geofence. New `POST /api/geofences/custom/{id}/activate` and `POST /api/geofences/custom/{id}/deactivate` endpoints with ownership validation. ([#36](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/36), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))

### Fixed
- **Geofence delete only removed area from active profile**: `DeleteAsync` and `AdminDeleteAsync` now remove the geofence area name from all profiles (`humans.area` + every `profiles.area` entry), not just the active one. ([#36](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/36), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Area selection not profile-scoped**: `GET /api/areas` now reads from `profiles.area` for the current profile instead of the shared `humans.area` field. `PUT /api/areas` writes to both `humans.area` and `profiles.area`. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Profile switch didn't swap areas**: `SwitchProfile` now saves `humans.area` to the old profile's `profiles.area` and loads the new profile's `profiles.area` into `humans.area`, keeping PoracleJS in sync. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
- **Geofence area operations not profile-aware**: `AddAreaToHumanAsync` and `RemoveAreaFromHumanAsync` now update both `humans.area` and `profiles.area` to prevent area subscriptions appearing on the wrong profile. ([#38](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/38), [PR #37](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/37))
## [0.6.0] - 2026-03-23

### Added
- **In-app Help & User Guide page** ([PR #26](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/26))
- **EF Core migrations for PoracleWeb database**: Schema changes apply automatically on startup via `MigrateAsync()`. Includes `MariaDbHistoryRepository` to fix `GET_LOCK(-1)` incompatibility with MariaDB. ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))

### Changed
- **Migrate pweb_settings to structured tables**: Replace the generic key-value `pweb_settings` table in the Poracle DB with 4 properly structured tables in the PoracleWeb database ([#34](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/34), [PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
  - `site_settings` — typed admin settings with categories and value types
  - `webhook_delegates` — relational webhook-to-user delegation with composite unique index
  - `quick_pick_definitions` — structured quick pick presets with JSON filter columns
  - `quick_pick_applied_states` — per-user/profile applied state tracking
  - Automatic one-time data migration on first startup via `SettingsMigrationStartupService`
  - Controllers use `ISiteSettingService` + `IWebhookDelegateService` instead of generic `IPwebSettingService`
  - `QuickPickService` refactored to use dedicated repositories instead of loading all settings and filtering by key prefix
  - Significant query performance improvement: full table scans replaced with indexed lookups

### Deprecated
- **`pweb_settings` table** — The old key-value table in the Poracle DB is no longer written to. Data is automatically migrated to the new structured tables on first startup. The old `IPwebSettingService` and `PwebSettingEntity` remain registered for the migration service but should not be used for new code.

### Fixed
- **Remove hardcoded branding**, rename namespaces to Pgan.PoracleWebNet ([PR #32](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/32))
- **MkDocs build** — remove unsupported option, pin mkdocs<2 ([PR #31](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/31))
- **Security hardening** — IDOR, OAuth redirect, command injection, CORS, and code quality ([PR #28](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/28))
- **Settings endpoint security** — `GET /api/settings` now filters sensitive keys for non-admin users ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Settings upsert preserves metadata** — `PUT /api/settings/{key}` no longer overwrites `ValueType` and `Category` ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Duplicate webhook delegate handling** — `AddAsync` returns existing delegate instead of throwing 500 ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))
- **Quick pick removal with deleted definitions** — `alarm_type` stored in applied state so removal works even if the definition is later deleted ([PR #35](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/35))

### Security
- Settings API no longer serves `api_secret`, `telegram_bot_token`, `scan_db` to non-admin users
- Webhook delegates stored in proper relational table (no more arbitrary key injection via settings PUT endpoint)

## [0.5.5] - 2026-03-22

### Fixed
- **Dashboard profile card shows 'Default' when no profiles exist** — instead of the misleading "Profile 1" fallback ([#23](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/23), [PR #24](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/24))

## [0.5.4] - 2026-03-22

### Added
- **Dashboard profile quick-switch** — profile card now shows actual profile name with a dropdown menu to switch profiles without leaving the dashboard ([#19](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/19), [PR #22](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/22))

### Fixed
- **Location 404 for users without profile records** — `GET /api/location` now falls back to `humans` table coordinates when no profile exists, and `PUT /api/location` auto-creates a default profile. Affected 99% of users. ([#17](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/17), [PR #20](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/20))
- **Changelog workflow inserting entries under wrong release** — replaced grep/sed with awk scoped to the `[Unreleased]` block; fixed double-v prefix in release commit messages ([#18](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/18), [PR #21](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/21))

## [0.5.3] - 2026-03-22

### Fixed
- **Nested polygon click priority** — explicitly call `bringToFront()` on smaller polygons after rendering to ensure Leaflet SVG hit detection works correctly ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))

## [0.5.2] - 2026-03-22

### Fixed
- **Nested polygon selection on area map** — smaller polygons inside larger ones (e.g. "mikes" inside "west end") can now be clicked; polygons are sorted by area so smaller ones render on top ([#12](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/12), [PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))

### Changed
- Convert all logger calls to `[LoggerMessage]` source generators for improved performance ([#14](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/14), [PR #15](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/15))
- Fix locale-sensitive `ToString`/`Parse`/`StartsWith` calls, use typed HTTP header properties, replace generic exceptions in tests

## [0.5.1] - 2026-03-22

### Fixed
- Koji Poracle format URL missing `?name=true` — geofence names were empty in the unified feed

## [0.5.0] - 2026-03-22

### Added
- **Unified Geofence Proxy Feed** — PoracleWeb is now the single geofence source for PoracleJS ([#10](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/10), [PR #11](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/11))
  - Fetches admin geofences from Koji, resolves groups from parent chain, merges user geofences
  - PoracleJS uses a single URL — no direct Koji connection needed, no `group_map.json` required
  - 5-minute in-memory cache with invalidation on geofence changes
  - Graceful degradation: user geofences still served if Koji is down; PoracleJS `.cache/` failover if PoracleWeb is down
  - No custom code needed in PoracleJS or Koji — compatible with upstream/standard versions

### Changed
- `Koji:ProjectName` is a new required config setting
- PoracleJS `geofence.path` simplified from dual-source array to single PoracleWeb URL

## [0.4.0] - 2026-03-22

### Added
- **Admin Poracle Server Management** — Monitor and restart PoracleJS instances remotely from the admin panel
  - Multi-server support with configurable servers (name, host, SSH user)
  - Real-time health status indicators (online/offline)
  - Per-server restart via SSH + PM2 with confirm dialog
  - Restart All option with warning about alert disruption
  - Dedicated "Poracle Servers" admin page
- **User Custom Geofences** — Draw polygon boundaries on a dedicated map page for precise notification zones ([#5](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/5))
  - Dedicated "My Geofences" page with Leaflet Draw polygon tools
  - Region auto-detection from polygon centroid
  - Geofence name validation (letters, numbers, spaces, hyphens, apostrophes, max 50 chars)
  - Max 10 custom geofences per user
  - Polygon limits (3-500 points)
- **Admin Geofence Management** — Review, approve, reject, and delete user-submitted geofences
  - Status filter tabs (All, Pending, Active, Approved, Rejected)
  - Approve with optional rename, reject with reason
  - Admin delete with full cleanup (DB, Koji, user areas)
- **Discord Forum Integration** — Geofence submissions create forum posts in coverage-requests channel
  - Auto-created forum tags (Geofence - Pending / Approved / Rejected)
  - Static map image of geofence polygon in embed
  - Approval/rejection messages posted to thread, then locked and archived
- **Shared Region Selector** — Unified autocomplete component with country/state grouping
  - Replaces 3 separate region selector implementations
  - Used in area-map, geofence-name-dialog, and area-list
- **PoracleWeb Database** — Separate `poracle_web` MariaDB database for app-owned data
  - `user_geofences` table with submission workflow fields
  - `PoracleWebContext` as second EF Core DbContext
- **Unified Geofence Proxy Feed** — `GET /api/geofence-feed` serves all geofences to PoracleJS from a single endpoint
  - PoracleWeb fetches admin geofences from Koji (cached 5 minutes, invalidated on changes), resolves group names from parent chain, and merges with user geofences
  - PoracleJS uses a single URL (`geofence.path`) — no direct Koji connection needed, no `group_map.json` required
  - Graceful degradation: user geofences still served if Koji is unreachable; PoracleJS `.cache/` provides failover if PoracleWeb is down
  - User geofences served with `displayInMatches: false` for privacy
- **Submit for Review Workflow** — Users can submit geofences for admin promotion to public areas
  - Clear messaging explaining the promotion process
  - Status badges on geofence cards (Pending, Approved, Rejected)

### Changed
- PoracleWeb is now the single geofence source for PoracleJS — admin geofences from Koji and user geofences from the local DB are merged into one feed. PoracleJS `geofence.path` is a single URL (not an array). `group_map.json` is no longer needed. `Koji:ProjectName` is a new required config for the feed.
- Areas page no longer shows user-created geofences (privacy fix)
- Geofence names stored lowercase for Poracle case-sensitive matching

### Fixed
- Leaflet Draw `draw:created` event binding uses string literal instead of `L.Draw.Event.CREATED`
- `displayInMatches` set to `false` on user geofences to prevent name leaking in DMs
- Discord notification tag cache now uses static fields (persists across transient instances)
- Server-side displayName validation matches frontend regex
- Koji `RemoveGeofenceFromProjectAsync` no longer sends `__parent: 0` (caused Koji 500)
- Polygon deserialization uses clean coordinate arrays instead of `JsonElement.Clone()`

## [0.3.0] - 2026-03-21

### Fixed
- **Pokemon IV validation**: ATK/DEF/STA fields now enforce 0-15 range with error messages ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))

### Added
- **Pokemon type filter**: Filter by Pokemon type with UICONS type icons from masterfile ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))
- **Tile grid selector**: Clickable tile grid for bulk Pokemon selection when gen/type filter is active

## [0.2.0] - 2026-03-21

### Fixed
- **Alerts saving to wrong profile**: All alarm types now use profile from JWT claim instead of hardcoded profile 1 ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))
- **Location not profile-scoped**: Location reads/writes from profiles table with dual-write to humans for PoracleJS compatibility ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))

### Changed
- `ProfileNo` removed from all frontend Create DTOs — enforced server-side from JWT
- Profile switching syncs lat/lon to humans table

## [0.1.0] - 2026-03-20

### Added
- Initial release of PoracleWeb
- Discord OAuth2 and Telegram authentication
- Pokemon, Raid, Quest, Invasion, Lure, Nest, Gym alarm management
- Area selection with interactive geofence map
- Location management with geocoding
- Profile system with per-profile locations and areas
- Bulk operations (distance update, delete) on all alarm types
- Admin panel for user management
- Dark/light theme with customizable accent colors
- Onboarding wizard for new users
- Skeleton loading animations
- Rate limiting (per-IP) on auth endpoints
- Docker deployment with Watchtower auto-updates

[Unreleased]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.4...v1.0.0
[0.6.4]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.3...v0.6.4
[0.6.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.1...v0.6.3
[0.6.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.0...v0.6.1
[0.6.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.5...v0.6.0
[0.5.5]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.4...v0.5.5
[0.5.4]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.3...v0.5.4
[0.5.3]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.2...v0.5.3
[0.5.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/releases/tag/v0.1.0
