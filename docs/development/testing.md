# Testing

## Frontend tests (Jest)

```bash
cd Applications/Pgan.PoracleWebNet.App/ClientApp
npm test                       # all specs
npm test -- quiet-chip         # only specs whose path matches
npm test -- --ci               # what CI runs
```

Jest with `jest-preset-angular`, configured in `jest.config.js`. `npm test` is a plain `jest`
invocation, so anything after `--` goes to Jest. Around 127 spec files cover:

- Services (`user-geofence.service.spec.ts`, `admin-geofence.service.spec.ts`, `profile.service.spec.ts`, `mute.service.spec.ts`)
- Components (`region-selector.component.spec.ts`, `geofence-submissions.component.spec.ts`)
- Dialogs (`geofence-name-dialog.component.spec.ts`, `geofence-approval-dialog.component.spec.ts`, `active-hours-editor-dialog.component.spec.ts`)
- Utilities (`geo.utils.spec.ts`, `active-hours.models.spec.ts`)
- Active hours (`active-hours-chip.component.spec.ts`, `location-warning.component.spec.ts`)
- Pipes
- Pokemon availability (`pokemon-availability.service.spec.ts`)
- Quiet periods (`quiet-chip.component.spec.ts`, `quiet-sheet.component.spec.ts`, `quiet-list-sheet.component.spec.ts`, and `quiet-surface-parity.spec.ts`, which pins the six list templates that carry the chip and the four that must not)
- Pokéstop Events (`pokestop-event-add-dialog.component.spec.ts`, `pokestop-event-edit-dialog.component.spec.ts`, `pokestop-event-list.component.spec.ts`)
- Rule summaries (`rule-summary.component.spec.ts`, `rule-summary.spec.ts`)
- Version-gated controls (`quest-add-dialog.pokecoins.spec.ts`)

## Backend tests (xUnit)

```bash
dotnet test                                      # all tests, from the solution root
dotnet test --filter FullyQualifiedName~MuteController   # one class
```

xUnit with Moq, one project (`Tests/Pgan.PoracleWebNet.Tests`) of around 136 test classes. They cover:

- Controllers (`UserGeofenceControllerTests`, `AdminGeofenceControllerTests`, `GeofenceFeedControllerTests`, `LocationControllerTests`, `ProfileControllerTests`, `AreaControllerTests`, `AdminControllerTests`, `SettingsControllerTests`, `ScannerControllerTests`, `PokemonAvailabilityControllerTests`, and all alarm controller tests)
- Alarm services (`MonsterServiceTests`, `RaidServiceTests`, `EggServiceTests`, `QuestServiceTests`, `InvasionServiceTests`, `LureServiceTests`, `NestServiceTests`, `GymServiceTests`) -- these mock `IPoracleTrackingProxy`
- Proxy classes (`PoracleTrackingProxyTests`, `PoracleHumanProxyTests`, `PoracleMuteProxyTests`) -- verify HTTP request construction, URL encoding, response unwrapping
- Human/profile services (`HumanServiceTests`, `ProfileServiceTests`) -- mock `IPoracleHumanProxy` for single-user ops, `IHumanRepository` for admin bulk ops
- Active hours validation (`ActiveHoursValidationTests`) -- server-side active hours validation rules
- Other services (`UserGeofenceServiceTests`, `DiscordNotificationServiceTests`, `GeoMathTests`, `CleaningServiceTests`, `DashboardServiceTests`, `SiteSettingServiceTests`, `WebhookDelegateServiceTests`, `SettingsMigrationServiceTests`, `QuickPickServiceSecurityTests`, `PokemonAvailabilityServiceTests`)
- Version gating (`MuteCapabilityServiceTests`, `QuestPokecoinCapabilityServiceTests`, `QuestPokecoinCapabilityTests`, `PoracleUnsupportedExceptionFilterTests`) -- each capability service fails closed, and a shortfall answers 409 with what the server needs
- v2 wire shapes (`PoracleProblemDetailsDescribeTests`, `AlarmDescriptionPassthroughTests`, `MuteControllerTests`)
- Mapping extensions (`MappingExtensionTests`) -- alarm DTO `To*()` / `ApplyUpdate()` and entity `ToModel()` / `ToEntity()` / `ApplyTo()`

!!! info "Alarm service tests mock IPoracleTrackingProxy"
    Since alarm services no longer use repositories, their tests mock `IPoracleTrackingProxy` instead of `IRepository`. The mock returns `JsonElement` values matching PoracleNG's snake_case JSON format.

!!! info "Human/profile tests mock IPoracleHumanProxy"
    `HumanServiceTests` and `ProfileServiceTests` mock `IPoracleHumanProxy` for single-user operations (get, create, exists, location, areas, profile switch, active hours). Admin bulk operations still mock `IHumanRepository`. `LocationControllerTests` and `AreaControllerTests` verify proxy calls with no direct DB interaction. `ProfileControllerTests` and `ProfileServiceTests` include extended coverage for active hours CRUD and validation.

## Tests that fail the build when a pattern comes back

Three test classes assert on source and attributes rather than behaviour, because the thing they guard
against is a future edit no behavioural test would notice:

- `DisabledAlarmTypeGatingTests` — `[RequireFeatureEnabled]` stays on the alarm controller *class*.
  Moving it onto individual actions leaves a page reachable for a type the operator switched off, which
  is what #784 did and #792 reverted.
- `FeatureGateCoverageTests` — every `disable_*` constant on `DisableFeatureKeys` has a controller
  enforcing it. A toggle wired only into the SPA leaves the endpoint open to a direct call.
- `NoAliasedDeleteTests` — `ExecuteDeleteAsync` appears nowhere under `Core/`, `Data/` or the API
  project. MariaDB answers 1064 to the aliased `DELETE` that `MySql.EntityFrameworkCore` emits, and the
  repository tests run on SQLite, which accepts it — so this reaches production green and fails on every
  call. The OIDC session cleanup shipped that way and had never once run (#707).

`quiet-surface-parity.spec.ts` is the frontend equivalent: it reads the templates and pins the six
surfaces that carry the quiet chip (five alarm lists plus the Areas page) against the four that must
not.

## Angular templates need a build, not just a test run

Jest and `tsc` do not resolve a component's template imports. A missing pipe, an unimported component
or a bitwise `&` in a template compiles clean under both and fails only in `npm run build`, which CI
runs as a separate step after Jest. Run it locally before pushing.

## Auditing fixes for the defects they introduce

Roughly one in five defects found in this project's audit sweeps was caused by an *earlier fix in the
same sweep*. They cluster into two shapes: a constraint added without enumerating who legitimately
depended on the loose rule, and a fix applied to one member of a set of eleven while ten siblings are
left alone.

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
