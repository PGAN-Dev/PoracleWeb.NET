# Database

PoracleWeb uses two separate MySQL databases and optionally connects to a third scanner database.

## Database contexts

### PoracleContext

The primary EF Core context connecting to the existing **Poracle database** managed by PoracleJS.

- Connection string: `ConnectionStrings:PoracleDb`
- Contains: `humans`, `monsters`, `raid`, `quest`, `invasion`, `lure`, `nest`, `gym`, `egg`, `profile` tables
- **Read-write** — PoracleWeb manages alarm filters and user settings but does not modify the schema

!!! warning "MySQL provider"
    This project uses `MySql.EntityFrameworkCore` (Oracle's official provider), **not** Pomelo (`Pomelo.EntityFrameworkCore.MySql`), which is incompatible with EF Core 10. Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

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

!!! info "MariaDB compatibility"
    `MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK(-1)` which returns NULL on MariaDB. The `MariaDbHistoryRepository` class overrides the lock to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

### RdmScannerContext (optional)

Connects to an RDM scanner database for nest/Pokemon data.

- Connection string: `ConnectionStrings:ScannerDb`
- If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses

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

## Entity conventions

### NULL string columns

Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method in `BaseRepository` handles this:

```csharp
// Sets all null string properties to "" before saving
protected void EnsureNotNullDefaults(TEntity entity)
```

Call this before any save operation to avoid MySQL constraint violations.

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
| `alarm_type` | varchar(20) | `monster`, `raid`, `egg`, `quest`, `invasion`, `lure`, `nest`, `gym` |
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
