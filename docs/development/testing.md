# Testing

## Frontend tests (Jest)

```bash
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm test
```

Uses Jest with `jest-preset-angular`. Tests cover:

- Services (`user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `profile.service.spec.ts`)
- Components (`region-selector.component.spec.ts`, `geofence-submissions.component.spec.ts`)
- Dialogs (`geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`, `active-hours-editor-dialog.component.spec.ts`)
- Utilities (`geo.utils.spec.ts`, `active-hours.models.spec.ts`)
- Active hours (`active-hours-chip.component.spec.ts`, `location-warning.component.spec.ts`)
- Pipes
- Pokemon availability (`pokemon-availability.service.spec.ts`)

## Backend tests (xUnit)

```bash
dotnet test
```

Uses xUnit with Moq. Tests cover:

- Controllers (`UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `LocationControllerTests`, `ProfileControllerTests`, `AreaControllerTests`, `AdminControllerTests`, `SettingsControllerTests`, `ScannerControllerTests`, `PokemonAvailabilityControllerTests`, and all alarm controller tests)
- Alarm services (`MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`) -- these mock `IPoracleTrackingProxy`
- Proxy classes (`PoracleTrackingProxyTests`, `PoracleHumanProxyTests`) -- verify HTTP request construction, URL encoding, response unwrapping
- Human/profile services (`HumanServiceTests`, `ProfileServiceTests`) -- mock `IPoracleHumanProxy` for single-user ops, `IHumanRepository` for admin bulk ops
- Active hours validation (`ActiveHoursValidationTests`) -- server-side active hours validation rules
- Other services (`UserGeofenceServiceTests`, `DiscordNotificationServiceTests`, `GeoMathTests`, `CleaningServiceTests`, `DashboardServiceTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `QuickPickServiceSecurityTests`, `PokemonAvailabilityServiceTests`)
- Mapping extensions (`MappingExtensionTests`) -- alarm DTO `To*()` / `ApplyUpdate()` and entity `ToModel()` / `ToEntity()` / `ApplyTo()`

!!! info "Alarm service tests mock IPoracleTrackingProxy"
    Since alarm services no longer use repositories, their tests mock `IPoracleTrackingProxy` instead of `IRepository`. The mock returns `JsonElement` values matching PoracleNG's snake_case JSON format.

!!! info "Human/profile tests mock IPoracleHumanProxy"
    `HumanServiceTests` and `ProfileServiceTests` mock `IPoracleHumanProxy` for single-user operations (get, create, exists, location, areas, profile switch, active hours). Admin bulk operations still mock `IHumanRepository`. `LocationControllerTests` and `AreaControllerTests` verify proxy calls with no direct DB interaction. `ProfileControllerTests` and `ProfileServiceTests` include extended coverage for active hours CRUD and validation.

## Auditing fixes for the defects they introduce

Roughly one in five defects found in this project's audit sweeps was caused by an *earlier fix in the
same sweep*. They cluster into two shapes: a constraint added without enumerating who legitimately
depended on the loose rule, and a fix applied to one member of a set of ten while nine siblings are left
alone.

`.claude/commands/regression-lens.md` is a Claude Code slash command (`/regression-lens`) that audits
recent merges asking only *what did these fixes break, and which siblings did they miss*. Run it after a
batch of fixes, scoping each pass to the previous pass's changes, until a pass reports nothing. When it
was first used it converged 8 → 5 → 2 → 1 → 0; stopping after one pass would have left five defects live,
including a profile-create path that answered 400 while leaving an orphan profile behind.

Two habits that came out of it are worth applying by hand, with or without the tool:

- **Give every guard a legitimate-case-still-passes test**, not just a refusal test. Check what real data
  looks like before tightening a rule — an invasion grunt-type allowlist would have refused `blanche` and
  `npc 0`, both of which exist in production.
- **Revert the fix and confirm the new test goes red.** A test written alongside a fix encodes that fix's
  own assumptions and passes either way. One spec in this repo was asserting a broken request shape, so
  the suite was defending the bug rather than catching it.

The full rationale is in the "Fixing Defects Without Causing Them" section of `CLAUDE.md`.

## CI

Both test suites run automatically on pushes and pull requests for **both** `main` and `develop`, and
against the merge queue. Since pull requests target `develop`, a workflow filtered to `main` alone would
mean PRs merged with no checks at all. See [CI/CD](ci-cd.md) for workflow details.
