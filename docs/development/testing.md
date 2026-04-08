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
- Utilities (`geo.utils.spec.ts`, `active-hours.utils.spec.ts`)
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
- Active hours validation (`ActiveHoursValidationTests`) -- 17 tests for server-side active hours validation rules
- Other services (`UserGeofenceServiceTests`, `CleaningServiceTests`, `DashboardServiceTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `QuickPickServiceSecurityTests`, `PokemonAvailabilityServiceTests`)
- AutoMapper mappings (non-alarm entities)

!!! info "Alarm service tests mock IPoracleTrackingProxy"
    Since alarm services no longer use repositories, their tests mock `IPoracleTrackingProxy` instead of `IRepository`. The mock returns `JsonElement` values matching PoracleNG's snake_case JSON format.

!!! info "Human/profile tests mock IPoracleHumanProxy"
    `HumanServiceTests` and `ProfileServiceTests` mock `IPoracleHumanProxy` for single-user operations (get, create, exists, location, areas, profile switch, active hours). Admin bulk operations still mock `IHumanRepository`. `LocationControllerTests` and `AreaControllerTests` verify proxy calls with no direct DB interaction. `ProfileControllerTests` and `ProfileServiceTests` include extended coverage for active hours CRUD and validation.

## CI

Both test suites run automatically on push/PR to `main` via GitHub Actions. See [CI/CD](ci-cd.md) for workflow details.
