# Database

PoracleWeb.NET uses two separate MySQL databases and optionally connects to a third scanner database.

## Database contexts

### PoracleContext

The primary EF Core context connecting to the existing **Poracle database** managed by PoracleNG.

- Connection string: `ConnectionStrings:PoracleDb`
- Contains: `humans` and `profiles` (direct access), ten alarm tables, PoracleNG's `schema_migrations`, and the deprecated `pweb_settings` KV table. `PoracleContext` maps entities for eleven of those — `humans`, `profiles`, `pweb_settings` and eight alarm tables; `forts`, `maxbattle` and `schema_migrations` are reached by raw SQL, which needs no entity
- **Limited direct access** — Alarm tracking is proxied through `IPoracleTrackingProxy`, and single-user human/profile operations go through `IPoracleHumanProxy`. Direct access is confined to:

| Direct access | What and why |
|---|---|
| Admin bulk human operations | `GetAllAsync`, `DeleteUserAsync`, `UpdateAsync` — PoracleNG has no admin-list, admin-delete or generic update endpoint |
| Profile **rename** | `ProfileRepository.RenameAsync` — PoracleNG's profile update answers `{"status":"ok"}` and silently ignores `name` |
| User-geofence area writes | `IUserAreaDualWriter` on `humans.area` and `profiles.area` — PoracleNG's `setAreas` strips fences that are not user-selectable |
| Alarm `override_areas` | `IUserAreaDualWriter.SetAlarmOverrideAreasAsync` writes this one column on the ten alarm tables. It is the only alarm-table write PoracleWeb makes; everything else about a row goes through the proxy |
| `schema_migrations` read | `PoracleSchemaVersionReader` reads the applied migration number for the [server capability probe](backend.md#server-capability-probe) |
| `pweb_settings` | `PwebSettingRepository` still reads and writes the deprecated KV table, and startup runs one `ALTER TABLE pweb_settings MODIFY COLUMN value LONGTEXT NULL` so the old rows can hold JSON. Both exist only to feed `SettingsMigrationService` |

The user-geofence area writes and the `override_areas` write are tagged `HACK: trusted-set-areas` in code and explained in [Backend](backend.md#areas).

!!! warning "MySQL provider"
    This project uses `MySql.EntityFrameworkCore` (Oracle's official provider), **not** Pomelo (`Pomelo.EntityFrameworkCore.MySql`), which is incompatible with EF Core 10. Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

#### Notable columns in `profiles`

| Column | Type | Description |
|---|---|---|
| `active_hours` | TEXT, nullable | JSON array of activation time rules |

The `active_hours` column stores a JSON array defining when alarm delivery is active for a given profile. Each entry specifies a day and time:

```json
[
  {"day": 1, "hours": "09", "mins": "00"},
  {"day": 1, "hours": "17", "mins": "30"},
  {"day": 7, "hours": "10", "mins": "00"}
]
```

- `day` — ISO weekday (1 = Monday, 7 = Sunday)
- `hours` / `mins` — stored as **strings** (zero-padded, e.g. `"09"`, `"00"`)

!!! info "Managed by PoracleNG"
    The `active_hours` column is part of Poracle's own schema (managed by PoracleNG) — no PoracleWeb.NET migration is needed. PoracleWeb.NET reads and writes this field through the `IPoracleHumanProxy` API, not via direct DB access.

#### Notable columns on the alarm tables

Per-alarm delivery scope rests on two columns, present on all ten tables (`monsters`, `raid`, `egg`, `quest`, `invasion`, `lures`, `nests`, `gym`, `forts`, `maxbattle`). Both arrived in PoracleNG 5.1.0.

| Column | Type | Description |
|---|---|---|
| `override_location_label` | string, nullable | Saved-place label the alarm measures its radius from, instead of the profile pin. A label that no longer exists is not an error — PoracleNG falls through to the pin, so deleting a place widens its alarms rather than breaking them. |
| `override_areas` | JSON array, nullable | Areas the alarm is confined to, lowercase with spaces. Replaces the profile's area list outright rather than intersecting with it. **NULL when unset, never `[]`** — `parseOverrideAreas` reads an empty value back as no override, while `[]` is a list that matches nothing. |

The two are mutually exclusive, areas cannot coexist with a radius, and a place requires one. PoracleNG refuses all three combinations, and so does PoracleWeb before the write (see [Backend](backend.md#per-alarm-areas)).

Two more columns on `monsters` alone, also 5.1.0:

| Column | Type | Description |
|---|---|---|
| `pvp_ranking_evolution` | int, default 0 | Which form the PVP ranks are read from: 0 base, 1 any mega, 2 Mega X, 3 Mega Y. Only consulted when a league is set. |
| `min_time` | int, default 0 | Seconds a spawn must still have left when it is found, or the alert is skipped. 0 means any. |

#### `user_locations`

Saved places live here, keyed by (human, label), and are written only through `IPoracleHumanProxy` — PoracleWeb makes no direct read or write to this table. See [Backend](backend.md#saved-places).

### PoracleWebContext

A separate EF Core context for **application-owned data**.

- Connection string: `ConnectionStrings:PoracleWebDb`
- Database: `poracle_web`
- Schema managed by **EF Core migrations** (`Database.MigrateAsync()` on startup)
- Does **not** modify the Poracle DB schema
- Contains:

| Table | Purpose |
|---|---|
| `user_geofences` | User-drawn custom geofence polygons |
| `site_settings` | Typed admin-configurable settings with categories |
| `webhook_delegates` | Relational webhook-to-user delegation mappings |
| `quick_pick_definitions` | Quick pick alarm presets (global and user-scoped) |
| `quick_pick_applied_states` | Tracks which quick picks users have applied per profile |
| `oidc_sessions` | Refresh-token families for OIDC silent refresh, with rotation and replay detection |

!!! info "MariaDB compatibility"
    `MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK(-1)` which returns NULL on MariaDB. The `MariaDbHistoryRepository` class overrides the lock to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

### ScannerContext (optional)

Connects to a Golbat scanner database for nest, Pokemon, and gym data.

- Connection string: `ConnectionStrings:ScannerDb`
- If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses
- Contains entity mappings for the scanner `gym` table (see [Scanner entities](#scanner-entities) below)

## EF Core migrations

The `poracle_web` database uses EF Core migrations for schema management. Tables and indexes are created automatically on startup.

### Adding a new migration

```bash
dotnet ef migrations add <MigrationName> \
  --context PoracleWebContext \
  --project Data/Pgan.PoracleWebNet.Data \
  --startup-project Applications/Pgan.PoracleWebNet.Api \
  --output-dir Migrations/PoracleWeb
```

Migrations are stored in `Data/Pgan.PoracleWebNet.Data/Migrations/PoracleWeb/`.

A design-time factory (`PoracleWebContextDesignTimeFactory`) provides the context for tooling without requiring a running app.

### Automatic application

On startup, `Program.cs` calls `webDb.Database.MigrateAsync()` which applies any pending migrations. New tables and indexes are created automatically — no manual SQL required.

## Scanner entities

### ScannerGymEntity

Maps to the `gym` table in the Golbat scanner database.

| Property | Column | Type | Description |
|---|---|---|---|
| `Id` | `id` | varchar (PK) | Gym fort ID |
| `Name` | `name` | varchar, nullable | Gym display name |
| `Url` | `url` | varchar, nullable | Gym photo thumbnail URL |
| `Lat` | `lat` | double | Latitude |
| `Lon` | `lon` | double | Longitude |
| `TeamId` | `team_id` | int, nullable | Controlling team (0 = neutral, 1 = Mystic, 2 = Valor, 3 = Instinct) |

### GymSearchResult

DTO model in `Core.Models` used by scanner gym search endpoints. Projected from `ScannerGymEntity` with an additional computed `Area` field.

| Property | Type | Description |
|---|---|---|
| `Id` | string | Gym fort ID |
| `Name` | string? | Gym display name |
| `Url` | string? | Gym photo thumbnail URL |
| `Lat` | double | Latitude |
| `Lon` | double | Longitude |
| `TeamId` | int? | Controlling team ID |
| `Area` | string? | Geofence area the gym belongs to (resolved at query time) |

## Entity conventions

### NULL string columns

!!! info "Alarm entities no longer written directly"
    Alarm tracking writes go through the PoracleNG API proxy, which handles NULL defaults via `cleanRow()`. The generic `BaseRepository` and its `EnsureNotNullDefaults()` method have been removed. Remaining direct-DB repositories (`HumanRepository` for admin ops, `poracle_web`-owned tables) handle null normalization as needed.

Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. For the few remaining direct-DB writes (admin human operations), repositories handle null-to-empty-string normalization as needed.

### gym_id semantics

The `gym_id` column on alarm entities (`raid`, `egg`, `gym`) uses NULL vs non-NULL to distinguish general alarms from gym-specific alarms:

- `gym_id = NULL` — general alarm, matches **all** gyms
- `gym_id = '<id>'` — gym-specific alarm, matches only the gym with that ID

An empty string (`''`) is **not** a valid value. It would be treated as a specific gym filter that matches nothing, silently breaking the alarm. PoracleNG handles this normalization on its side for alarm writes.

## Site settings table

The `site_settings` table replaces the deprecated `pweb_settings` key-value store. Settings are typed and categorized:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `category` | varchar(50) | Setting group: `branding`, `features`, `alarms`, `admin`, `commands`, `telegram`, `maps`, `analytics`, `debug`, `icons` |
| `key` | varchar(100) | Unique setting key (e.g., `custom_title`, `disable_mons`) |
| `value` | text | Setting value |
| `value_type` | varchar(20) | Type hint: `string`, `boolean`, `url`, `csv` |

### Data migration

On first startup, `SettingsMigrationStartupService` automatically migrates data from the old `pweb_settings` table (Poracle DB) to the new structured tables. This is idempotent — a `migration_completed` sentinel prevents re-running.

## Webhook delegates table

Relational table replacing the `webhook_delegates:{id}` key pattern:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `webhook_id` | varchar(500) | Webhook URL/identifier |
| `user_id` | varchar(100) | Delegated user ID |
| `created_at` | datetime | When the delegation was created |

Unique composite index on `(webhook_id, user_id)`. Additional index on `user_id` for login-flow lookups.

## Quick pick tables

### quick_pick_definitions

Stores alarm presets (both admin-global and user-scoped):

| Column | Type | Description |
|---|---|---|
| `id` | varchar(50) (PK) | Unique pick ID |
| `name` | varchar(200) | Display name |
| `alarm_type` | varchar(20) | `monster`, `raid`, `egg`, `quest`, `invasion`, `lure`, `nest`, `gym`, `maxbattle` |
| `scope` | varchar(10) | `global` (admin) or `user` |
| `owner_user_id` | varchar(100) | NULL for global, user ID for user-scoped |
| `filters_json` | JSON | Alarm filter parameters |
| `sort_order` | int | Display ordering |
| `enabled` | bool | Whether the pick is active |

Composite index on `(scope, owner_user_id)` for efficient filtering.

### quick_pick_applied_states

Tracks which picks users have applied:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `user_id` | varchar(100) | User who applied the pick |
| `profile_no` | int | Profile the pick was applied to |
| `quick_pick_id` | varchar(50) | The applied pick |
| `alarm_type` | varchar(20) | Alarm type stored at apply time (for safe removal even if definition is deleted) |
| `tracked_uids_json` | JSON | UIDs of created alarm rows |
| `exclude_pokemon_ids_json` | JSON | Pokemon IDs excluded at apply time |

Unique composite index on `(user_id, profile_no, quick_pick_id)`.

## User geofences table

The `user_geofences` table stores user-drawn polygon geofences:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `human_id` | string | Owner's Discord/Telegram ID |
| `display_name` | string | User-provided name |
| `koji_name` | string | Lowercase Poracle-compatible name |
| `polygon_json` | text | Array of lat/lng coordinates |
| `status` | string | `active`, `pending_review`, `approved`, `rejected` |
| `group_name` | string | Region/group name |
| `review_notes` | string | Admin notes on approval/rejection |
| `discord_thread_id` | string | Discord forum thread ID |
| `created_at` | datetime | Creation timestamp |
| `updated_at` | datetime | Last update timestamp |
