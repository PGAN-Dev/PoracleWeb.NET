# Changelog

All notable changes to PoracleWeb are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.2] - 2026-03-22

### Fixed
- explicitly bringToFront smaller polygons for click priority ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))
- **Nested polygon selection on area map** — smaller polygons inside larger ones (e.g. "mikes" inside "west end") can now be clicked; polygons are sorted by area so smaller ones render on top ([#12](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/12), [PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))

### Changed
- Convert all logger calls to `[LoggerMessage]` source generators for improved performance ([#14](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/14), [PR #15](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/15))
- Fix locale-sensitive `ToString`/`Parse`/`StartsWith` calls, use typed HTTP header properties, replace generic exceptions in tests

## [0.5.1] - 2026-03-22

### Fixed
- explicitly bringToFront smaller polygons for click priority ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))
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
- Unified geofence proxy feed — PoracleWeb as single geofence source ([PR #11](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/11))
- Admin remote Poracle server restart ([PR #8](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/8))
- User-drawn custom geofences with admin review workflow ([PR #6](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/6))
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
- explicitly bringToFront smaller polygons for click priority ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))
- nested polygon selection on area map ([PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))
- Leaflet Draw `draw:created` event binding uses string literal instead of `L.Draw.Event.CREATED`
- `displayInMatches` set to `false` on user geofences to prevent name leaking in DMs
- Discord notification tag cache now uses static fields (persists across transient instances)
- Server-side displayName validation matches frontend regex
- Koji `RemoveGeofenceFromProjectAsync` no longer sends `__parent: 0` (caused Koji 500)
- Polygon deserialization uses clean coordinate arrays instead of `JsonElement.Clone()`

## [0.3.0] - 2026-03-21

### Fixed
- explicitly bringToFront smaller polygons for click priority ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))
- nested polygon selection on area map ([PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))
- **Pokemon IV validation**: ATK/DEF/STA fields now enforce 0-15 range with error messages ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))

### Added
- Unified geofence proxy feed — PoracleWeb as single geofence source ([PR #11](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/11))
- Admin remote Poracle server restart ([PR #8](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/8))
- User-drawn custom geofences with admin review workflow ([PR #6](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/6))
- **Pokemon type filter**: Filter by Pokemon type with UICONS type icons from masterfile ([#3](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/3), [PR #4](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/4))
- **Tile grid selector**: Clickable tile grid for bulk Pokemon selection when gen/type filter is active

## [0.2.0] - 2026-03-21

### Fixed
- explicitly bringToFront smaller polygons for click priority ([PR #16](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/16))
- nested polygon selection on area map ([PR #13](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/13))
- **Alerts saving to wrong profile**: All alarm types now use profile from JWT claim instead of hardcoded profile 1 ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))
- **Location not profile-scoped**: Location reads/writes from profiles table with dual-write to humans for PoracleJS compatibility ([#1](https://github.com/PGAN-Dev/PoracleWeb.NET/issues/1), [PR #2](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/2))

### Changed
- `ProfileNo` removed from all frontend Create DTOs — enforced server-side from JWT
- Profile switching syncs lat/lon to humans table

## [0.1.0] - 2026-03-20

### Added
- Unified geofence proxy feed — PoracleWeb as single geofence source ([PR #11](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/11))
- Admin remote Poracle server restart ([PR #8](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/8))
- User-drawn custom geofences with admin review workflow ([PR #6](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/6))
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

[Unreleased]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.2...HEAD
[0.5.2]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.1...v0.5.2
[0.5.1]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.3.0...v0.5.0
[0.4.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.2.0...v0.4.0
[0.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/releases/tag/v0.1.0
