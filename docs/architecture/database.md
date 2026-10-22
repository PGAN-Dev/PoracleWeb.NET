# Database

PoracleWeb.NET owns one MySQL database, `poracle_web`, and optionally reads a second, scanner database. It does not connect to Poracle's database. Everything Poracle stores (users, profiles, alarms, saved places) is reached through PoracleNG's REST API.

## Database contexts

### Poracle's data (not connected)

PoracleNG keeps users, profiles and alarms in its own database. PoracleWeb.NET has no connection string for it and no `DbContext` mapping it, so a direct write to an alarm table isn't possible from this application. The older `DB_*` variables are ignored (see [Configuration](../configuration/reference.md#databases)).

The rest of this section describes what PoracleNG stores, because the shapes explain what the API returns. Treat it as a reference to PoracleNG's schema, not as something PoracleWeb.NET reads.

Area writes for user geofences (Areas save, create, toggle, delete, rename, approval) go through PoracleNG's `/api/v2` area routes. Saved places live in PoracleNG too, keyed by (human, label), and are written only through `IPoracleHumanProxy`.

!!! note "Eleven tracking types, ten alarm tables"
    Ten is the table count and it is right. The eleventh type, Pokéstop Events (`incident`), stores its
    rows in the existing `invasion` table. No table arrived with it, and nothing marks the row apart
    except `grunt_type`, which holds an event name (`gold-stop`, `kecleon`, `showcase`) rather than a
    grunt name — the same discriminator PoracleNG's own `isEventGruntType` uses to split its two v2
    endpoints.

    **v1 applies no such filter**, so a Showcase rule created through `/api/v2/.../tracking/incident`
    comes straight back out of `GET /api/tracking/invasion/{id}`, verified against 5.2.1 and 5.4.0.
    `InvasionService` therefore does the partition itself — but only when the Pokéstop Events page
    exists. Where it does not (Poracle reports `disable_showcase = true`), the event rows stay in the
    invasion list, because filtering them out of the only page that can show them would leave alarms
    that fire and cannot be deleted.

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

An entry that repeats across a window carries three more fields, all snake_case and all omitted on a
single fire: `step` (whole hours, 1–23), `end_hours` and `end_mins`. `{"day":1,"hours":9,"mins":0,
"end_hours":17,"end_mins":0,"step":2}` fires at 09:00 and every two hours through 17:00. See
[Active hours](backend.md#active-hours) for what is validated.

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

#### Costume

`costume` arrived after 5.1.0 and on two tables at different times: `monsters.costume` at PoracleNG
database migration **6**, `raid.costume` at migration **7**. Both are in every supported release (5.2.1
and later).

| Value | Meaning |
|---|---|
| `9000` | Any costume |
| `0` | No costume |
| _n_ | That costume |

The filter is always offered: every supported PoracleNG stores it. It used to be gated on the applied
migration number, read straight from `schema_migrations`, and later on a capability endpoint; neither exists
any more. See [Version compatibility](poracleng-compatibility.md#how-support-is-decided).

#### `user_locations`

PoracleNG stores saved places here, keyed by (human, label). PoracleWeb.NET reads and writes them only through `IPoracleHumanProxy`. See [Backend](backend.md#saved-places).

### PoracleWebContext

A separate EF Core context for **application-owned data**.

- Connection string: `ConnectionStrings:PoracleWebDb`
- Database: `poracle_web`
- Schema managed by **EF Core migrations** (`Database.MigrateAsync()` on startup)
- PoracleWeb.NET's only database; Poracle's is never touched
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

### gym_id semantics

The `gym_id` column on alarm entities (`raid`, `egg`, `gym`) uses NULL vs non-NULL to distinguish general alarms from gym-specific alarms:

- `gym_id = NULL` — general alarm, matches **all** gyms
- `gym_id = '<id>'` — gym-specific alarm, matches only the gym with that ID

An empty string (`''`) is **not** a valid value. It would be treated as a specific gym filter that matches nothing, silently breaking the alarm. PoracleNG handles this normalization on its side for alarm writes.

## Site settings table

The `site_settings` table holds the admin-configurable settings. They're typed and categorized:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `category` | varchar(50) | Setting group — `branding`, `features`, `alarms`, `icons`, `admin`, `api`, `discord`, `oidc`, `telegram`, `maps`, `commands`, `analytics`, `debug`, plus `system` for a legacy sentinel older versions wrote |
| `key` | varchar(100) | Unique setting key (e.g., `custom_title`, `disable_mons`) |
| `value` | text | Setting value |
| `value_type` | varchar(20) | Type hint: `string`, `boolean`, `url`, `csv` |

### Defaults

At startup `SettingsMigrationService.SeedDefaultsAsync` inserts any missing default setting and repoints the retired icon packs. It only adds rows that don't exist, so running it again changes nothing.

The `pweb_settings` table that early versions kept in Poracle's database is no longer read, and the app never alters or drops it. An admin can drop it.

## Webhook delegates table

Relational table for webhook delegation:

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
