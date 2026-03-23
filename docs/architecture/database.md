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
- Contains: `user_geofences` table
- Schema managed by EF Core `EnsureCreated()` or migrations
- Does **not** modify the Poracle DB schema

### RdmScannerContext (optional)

Connects to an RDM scanner database for nest/Pokemon data.

- Connection string: `ConnectionStrings:ScannerDb`
- If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses

## Entity conventions

### NULL string columns

Many Poracle DB columns are `NOT NULL` with empty-string defaults, but EF Core maps them as `string?`. The `EnsureNotNullDefaults()` method in `BaseRepository` handles this:

```csharp
// Sets all null string properties to "" before saving
protected void EnsureNotNullDefaults(TEntity entity)
```

Call this before any save operation to avoid MySQL constraint violations.

## User geofences table

The `user_geofences` table stores user-drawn polygon geofences:

| Column | Type | Description |
|---|---|---|
| `id` | int (PK) | Auto-increment ID |
| `discord_id` | string | Owner's Discord ID |
| `display_name` | string | User-provided name |
| `koji_name` | string | Lowercase Poracle-compatible name |
| `polygon` | JSON | Array of lat/lng coordinates |
| `status` | string | `active`, `pending_review`, `approved`, `rejected` |
| `region` | string | Auto-detected region name |
| `review_notes` | string | Admin notes on approval/rejection |
| `discord_thread_id` | string | Discord forum thread ID |
| `created_at` | datetime | Creation timestamp |
| `updated_at` | datetime | Last update timestamp |
