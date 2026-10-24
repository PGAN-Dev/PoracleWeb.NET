---
paths:
  - "Data/**"
  - "Core/Pgan.PoracleWebNet.Core.Repositories/**"
  - "Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/**"
  - "**/SettingsMigration*"
  - "**/*Repository*.cs"
  - "**/Migrations/**"
  - "**/Tests/**/Repositories/**"
  - "**/NoAliasedDelete*"
  - "**/MariaDb*"
---

## PoracleWeb Database
- Second `DbContext`: `PoracleWebContext` using `ConnectionStrings:PoracleWebDb`.
- Separate `poracle_web` MariaDB/MySQL database for application-owned data (not managed by PoracleJS).
- PoracleWeb.NET has no connection to Poracle's database; it is managed entirely by PoracleNG.
- Tables:
  - `user_geofences` -- user-drawn custom geofence polygons.
  - `site_settings` -- typed admin-configurable settings with categories .
  - `webhook_delegates` -- relational user-to-webhook delegation mappings with composite unique constraint.
  - `quick_pick_definitions` -- structured quick pick alarm presets (global and user-scoped) with JSON filter columns.
  - `quick_pick_applied_states` -- tracks which quick picks users have applied per profile, with tracked alarm UIDs.
- Schema managed via **EF Core migrations** (`Database.MigrateAsync()` on startup). New tables are created automatically.
- `MariaDbHistoryRepository` overrides the default MySQL migration lock to use `GET_LOCK(3600)` instead of `GET_LOCK(-1)`, working around a `MySql.EntityFrameworkCore` bug where MariaDB returns NULL for infinite-timeout locks.
- Design-time factory (`PoracleWebContextDesignTimeFactory`) enables `dotnet ef migrations add` without a running app.
- Migrations are stored in `Data/Pgan.PoracleWebNet.Data/Migrations/PoracleWeb/`.

## MySQL Provider
Pomelo MySQL provider (`Pomelo.EntityFrameworkCore.MySql`) is **incompatible** with EF Core 10. This project uses `MySql.EntityFrameworkCore` (Oracle's official provider). Connection setup uses `options.UseMySQL(connectionString)` (capital SQL).

## Settings Seeding
At startup `SettingsMigrationStartupService` calls `SeedDefaultsAsync` (default site settings, the quick-pick seed marker, the dead icon pack repoint). It only adds what is missing. The one-time copy out of the `pweb_settings` KV table in Poracle's database was removed with the Poracle DB connection; an install older than 0.6.0 has to start any 2.x release once before taking this one. The table itself is never read, altered or dropped.

## MariaDB GET_LOCK Compatibility
`MySql.EntityFrameworkCore`'s `MigrateAsync()` uses `GET_LOCK('__EFMigrationsLock', -1)` which returns NULL on MariaDB (infinite timeout not supported), causing `System.InvalidCastException`. The `MariaDbHistoryRepository` class overrides the lock acquisition to use `GET_LOCK(3600)` instead. This is registered via `ReplaceService<IHistoryRepository, MariaDbHistoryRepository>()` on `PoracleWebContext`.

## `ExecuteDeleteAsync` Is Unusable On MariaDB
`MySql.EntityFrameworkCore` emits the aliased single-table form, ``DELETE FROM `t` AS `x` WHERE …``, and MariaDB answers 1064 — it requires the multi-table ``DELETE x FROM t AS x`` once an alias is present. `ExecuteUpdateAsync` is fine; MariaDB accepts `UPDATE t AS x SET x.c = …`. Verified against MariaDB 10.8.2.

Nothing catches this before production. It compiles, and the repository tests pass because they run on **SQLite**, whose provider emits the same alias and accepts it. The OIDC session cleanup shipped this way and had never once run (#707); `QuickPickAppliedStateRepository` hit it earlier and quietly grew a load-and-`RemoveRange` workaround.

Use raw SQL with **unquoted** identifiers (so the statement also parses on SQLite for the tests), or load and `RemoveRange` when the row count is small. `NoAliasedDeleteTests` fails the build if `.ExecuteDeleteAsync(` reappears anywhere under `Core/`, `Data/` or the API project.
