# PoracleNG 5.4.0 sub-project 4: no Poracle database Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PoracleWeb.NET never opens a connection to Poracle's database. `PoracleContext`, its entities, `ConnectionStrings:PoracleDb`, the `DB_*` settings and the `pweb_settings` migration path are gone, and a test fails the build if any of them comes back.

**Architecture:** Two code tasks remove the last two consumers of `PoracleContext` and then the context itself. The legacy `pweb_settings` → `poracle_web` settings migration (`SettingsMigrationService.MigrateAsync` and its helpers) goes first, while default seeding (`SeedDefaultsAsync`) stays. Then the context, its entities and mappings, the DI registration, the `DB_*` bridge and the startup check go. A third task updates setup scripts, examples and docs.

**Tech Stack:** .NET 10, EF Core (`PoracleWebContext` stays), xUnit, bash, mkdocs.

**Spec:** `docs/superpowers/specs/2026-10-21-poracleng-540-v2-migration-design.md`, "Sub-project 4: no Poracle database" and the "Docs" table row 4.

## Global Constraints

- Nothing under `Core/`, `Data/` or `Applications/Pgan.PoracleWebNet.Api/` may reference `PoracleContext`, `PwebSetting`, a `PoracleDb` connection string or map a `DB_HOST`/`DB_PORT`/`DB_NAME`/`DB_USER`/`DB_PASSWORD` variable. `WEB_DB_*` and `PoracleWebContext` stay, untouched.
- An install that still sets any `DB_*` variable or `ConnectionStrings__PoracleDb` starts normally and logs exactly one Information line at startup: `DB_* settings are no longer used by PoracleWeb.NET and can be removed.` (Do not refuse to start.)
- The `pweb_settings` table is never dropped or altered by the app. Docs say admins may drop it themselves.
- An install older than 0.6.0 must start any 2.x release once before upgrading (the guide says so), because the settings migration is removed.
- Default seeding (`SeedDefaultsAsync`: default settings, quick-pick seed marker, dead icon pack repoint) must keep running at startup exactly as today.
- "PoracleWeb.NET" in full in docs and the CHANGELOG; direct prose, contractions, at most one em dash per paragraph, no "**Bold term:** explanation" lists in new prose; no Claude session links or `Co-authored-by`; no plan task numbers in commit messages.
- C# line endings as the file has them (`git diff --numstat`); dotnet with `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true NUGET_PLUGIN_PATHS=` and `--no-restore`.

## Review Focus

1. **A fresh install with no `DB_*` at all** starts, migrates `poracle_web`, seeds defaults, and serves the SPA. Owned by Task 2 (startup test) and Task 4 (bed).
2. **An existing install still setting `DB_*`** starts and logs the one Information line. Owned by Task 2.
3. **Default seeding still runs** after the migration path is removed (quick picks seeded once, icon repoint). Owned by Task 1.
4. **Integration tests that boot the app** (`GeofenceFeedPipelineTests`, `ImpersonationPipelineTests`, `ControllerDependencyRegistrationTests`) no longer need a Poracle connection string. Owned by Task 2.
5. **The setup wizard** no longer asks for Poracle DB credentials, and the generated `.env` works. Owned by Task 3.

---

### Task 1: Remove the `pweb_settings` migration path

**Files:**
- Delete: `Core/Pgan.PoracleWebNet.Core.Abstractions/Repositories/IPwebSettingRepository.cs`, `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPwebSettingService.cs`, `Core/Pgan.PoracleWebNet.Core.Models/PwebSetting.cs`, `Core/Pgan.PoracleWebNet.Core.Repositories/PwebSettingRepository.cs`, `Core/Pgan.PoracleWebNet.Core.Services/PwebSettingService.cs`, `Tests/.../Services/PwebSettingServiceTests.cs`
- Modify: `Core/Pgan.PoracleWebNet.Core.Services/SettingsMigrationService.cs` (delete `MigrateAsync` and every helper and table only it uses — `MigrateWebhookDelegateAsync`, `MigrateAdminQuickPickAsync`, `MigrateUserQuickPickAsync`, `MigrateQuickPickAppliedStateAsync`, `MigrateSiteSettingAsync`, `CategoryMap`/`BooleanKeys` if nothing else uses them; drop the `IPwebSettingService` dependency), `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/ISettingsMigrationService.cs` (remove `MigrateAsync`), `Applications/Pgan.PoracleWebNet.Api/Services/SettingsMigrationStartupService.cs` (call `SeedDefaultsAsync` only; log line says seeding), `Core/Pgan.PoracleWebNet.Core.Mappings/EntityMappingExtensions.cs` (remove `PwebSetting` mappings), `Core/Pgan.PoracleWebNet.Core.Models/DisableFeatureKeys.cs` (doc comment step 3 no longer names the migration tables), `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs` (registrations), `Applications/Pgan.PoracleWebNet.Api/Program.cs` (delete the startup `ALTER TABLE pweb_settings` block and its `StartupLog` method), the admin settings SPA if any `FeatureKeys`/migration list mirrors `BooleanKeys` (check), tests: `SettingsMigrationServiceTests.cs` (keep and adapt the seeding tests, delete the migration tests), `DeadIconPackRepointTests.cs`, `MappingExtensionTests.cs` (PwebSetting cases).

**Interfaces:**
- Produces: `ISettingsMigrationService` with `Task SeedDefaultsAsync()` only (keep the type name to limit churn; its summary says it seeds defaults and repairs settings, nothing migrates from Poracle's database).

- [ ] **Step 1: Write the failing test**: `StartupRunsSeedingWithoutAPoracleDatabase` (the startup service resolves `ISettingsMigrationService` and calls `SeedDefaultsAsync` once; `MigrateAsync` no longer exists so the old call fails to compile — RED is the compile failure), plus keep every existing `SeedDefaultsAsync`/repoint/backfill test green.
- [ ] **Step 2: Implement** the deletions and edits.
- [ ] **Step 3: Run** the full backend suite → green; `grep -rn "PwebSetting\|pweb_settings\|MigrateAsync()" Core Data Applications --include=*.cs` → only `PwebSettingEntity` and `PoracleContext` remain (Task 2 removes them).
- [ ] **Step 4: Commit** `refactor: drop the pweb_settings settings migration; seeding stays`.

### Task 2: Remove `PoracleContext` and `DB_*`

**Files:**
- Delete: `Data/Pgan.PoracleWebNet.Data/PoracleContext.cs`, `Data/Pgan.PoracleWebNet.Data/Entities/HumanEntity.cs`, `ProfileEntity.cs`, `PwebSettingEntity.cs`
- Modify: `EntityMappingExtensions.cs` (remove Human/Profile entity mappings), `MappingExtensionTests.cs`, `ServiceCollectionExtensions.cs` (remove `AddDbContext<PoracleContext>`), `Program.cs` (remove `ComposeConnectionString("PoracleDb", …)`, the `ConnectionStrings:PoracleDb` startup check, and add the one-time Information line when any `DB_HOST`/`DB_PORT`/`DB_NAME`/`DB_USER`/`DB_PASSWORD` env var or `ConnectionStrings:PoracleDb` config is set), `Tests/.../Configuration/ControllerDependencyRegistrationTests.cs`, `Tests/.../Integration/GeofenceFeedPipelineTests.cs`, `ImpersonationPipelineTests.cs` (drop the PoracleDb settings), any health check or diagnostics that reported the Poracle DB (grep).
- Create: `Tests/Pgan.PoracleWebNet.Tests/Architecture/NoPoracleDatabaseTests.cs`

**Interfaces:**
- Produces: `NoPoracleDatabaseTests` — scans every `.cs` file under `Core/`, `Data/` and `Applications/Pgan.PoracleWebNet.Api/` (excluding `bin/`, `obj/`) and fails on any of: `PoracleContext`, `PwebSetting`, `"PoracleDb"`, `ConnectionStrings:PoracleDb`, `ConnectionStrings__PoracleDb`, a `MapEnvVar`/`ComposeConnectionString` naming `DB_HOST`. It asserts it scanned at least one file per root (so it can't pass by scanning nothing), and has a self-test that its matcher finds a planted string. The startup log line's own literal `DB_*` text is allowed (match the exact forms above, not the prose).

- [ ] **Step 1: Write the failing tests**: `NoPoracleDatabaseTests` (RED: it finds `PoracleContext` today); `StartsWithoutAPoracleConnectionString` (boot the app via the existing `WebApplicationFactory` setup with no `ConnectionStrings:PoracleDb` and no `DB_*`, `GET /api/version` → 200); `LogsOnceWhenLegacyDbSettingsArePresent` (set `DB_HOST`; the startup log contains the line once). Use the existing integration-test factory pattern.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Run** the full backend suite → green.
- [ ] **Step 4: Commit** `feat: PoracleWeb.NET no longer connects to Poracle's database`.

### Task 3: Setup scripts, examples, docs, CHANGELOG

**Files:**
- Modify: `scripts/setup.sh` (drop the Poracle DB questions and `set_env DB_*`; keep `WEB_DB_*`), `scripts/dev.sh` (any `DB_*` use; `db:create` stays for `poracle_web`), `.env.example` (remove the `DB_*` block), `docker-compose.yml.example` (comment names `DB_HOST` and `ConnectionStrings__PoracleDb` as bridge examples — use `WEB_DB_HOST`/`ConnectionStrings__PoracleWebDb`)
- Modify docs: `docs/configuration/reference.md` (no `DB_*`; one line that older installs can delete them), `docs/getting-started/quick-start.md`, `standalone-setup.md`, `development-setup.md`, `docs/architecture/database.md` (PoracleWeb.NET owns one database; Poracle's is reached only through PoracleNG's API), `docs/architecture/overview.md`, `docs/architecture/poracleng-proxy.md` (any "direct DB" remnants), `docs/configuration/site-settings.md` (legacy `pweb_settings` migration text), `docs/getting-started/upgrading-to-poracleng-5-4.md` (replace "`DB_*` is still required" with: `DB_*` is unused and can be removed; the `pweb_settings` table may be dropped by an admin; an install older than 0.6.0 must start any 2.x release once first), `docs/troubleshooting.md` (new "PoracleNG is older than 5.4.0" entry: symptoms — the Versions card shows the mismatch, alarm and profile calls fail naming the version they need; fix — upgrade PoracleNG; delete or rewrite "NULL string constraint violations" if it describes direct DB writes)
- Modify: `CLAUDE.md` (delete "NULL String Columns", the `PoracleContext`/`DB_*`/`pweb_settings` passages and File Locations rows, the dual-write explanations still present, and the `DB_*` part of the prod-query recipe in "Fixing Defects Without Causing Them" — keep the advice to query production read-only, without naming `.env` `DB_*` values), `CHANGELOG.md` (`[Unreleased]`: PoracleWeb.NET no longer connects to Poracle's database; `DB_*` unused; the `pweb_settings` migration removed, with the 0.6.0 note; edit earlier `[Unreleased]` lines that say `DB_*` is still required)

- [ ] **Step 1: Grep** `grep -rn -i "DB_HOST\|DB_PASSWORD\|PoracleDb\b\|pweb_settings\|PoracleContext\|direct DB\|directly to Poracle" docs README.md CLAUDE.md scripts .env.example docker-compose.yml.example` (excluding `docs/superpowers`, dated upstream-history pages and the CHANGELOG's released sections) and fix every hit that describes current behaviour.
- [ ] **Step 2: Edit**, then `bash -n scripts/setup.sh scripts/dev.sh`.
- [ ] **Step 3: Run** `mkdocs build --strict` → exit 0.
- [ ] **Step 4: Commit** `docs: no Poracle database; DB_* and pweb_settings retired`.

### Task 4: Bed verification (controller)

- [ ] Build the branch image on dev-01; start an instance with `instance.sh` and then remove `ConnectionStrings__PoracleDb` from its env file and restart, so it runs with no Poracle DB setting at all. Check startup logs (no errors; no info line), then add `DB_HOST=x` and check the info line appears once.
- [ ] Run the smoke harness and e2eC against it; no new failures against the 3d baseline (smoke 254/260).
- [ ] Record the results in the PR body.
