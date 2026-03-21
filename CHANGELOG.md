# Changelog

All notable changes to PoracleWeb are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- User-drawn custom geofences with admin review workflow ([PR #6](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/6))
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
- **Poracle Feed Endpoint** — `GET /api/geofence-feed` serves user geofences to PoracleJS
  - PoracleJS loads from both Koji (admin areas) and PoracleWeb feed (user geofences)
  - User geofences served with `displayInMatches: false` for privacy
- **Submit for Review Workflow** — Users can submit geofences for admin promotion to public areas
  - Clear messaging explaining the promotion process
  - Status badges on geofence cards (Pending, Approved, Rejected)

### Changed
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
- User-drawn custom geofences with admin review workflow ([PR #6](https://github.com/PGAN-Dev/PoracleWeb.NET/pull/6))
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

[Unreleased]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/PGAN-Dev/PoracleWeb.NET/releases/tag/v0.1.0
