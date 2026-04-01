# Testing

## Frontend tests (Jest)

```bash
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm test
```

Uses Jest with `jest-preset-angular`. Tests cover:

- Services (`user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`)
- Components (`region-selector.component.spec.ts`, `geofence-submissions.component.spec.ts`)
- Dialogs (`geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`)
- Utilities (`geo.utils.spec.ts`)
- Pipes

## Backend tests (xUnit)

```bash
dotnet test
```

Uses xUnit with Moq. Tests cover:

- Controllers (`UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`)
- Alarm services (`MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`) -- these mock `IPoracleTrackingProxy`
- Other services (`UserGeofenceServiceTests`, `CleaningServiceTests`, `DashboardServiceTests`, `HumanServiceTests`)
- AutoMapper mappings (non-alarm entities)

!!! info "Alarm service tests mock IPoracleTrackingProxy"
    Since alarm services no longer use repositories, their tests mock `IPoracleTrackingProxy` instead of `IRepository`. The mock returns `JsonElement` values matching PoracleNG's snake_case JSON format.

## CI

Both test suites run automatically on push/PR to `main` via GitHub Actions. See [CI/CD](ci-cd.md) for workflow details.
