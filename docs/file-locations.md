Detailed project tree and file lookup table, moved out of `CLAUDE.md` to keep its per-turn context small.

# Solution Structure

```
Pgan.PoracleWebNet.slnx
|
+-- Core/
|   +-- Core.Abstractions/       Interfaces: IRepository, IService, IPoracleTrackingProxy,
|   |                            IPoracleHumanProxy, ITestAlertService,
|   |                            IGolbatApiProxy, IPokemonAvailabilityService
|   +-- Core.Models/             DTOs passed between layers (not EF entities)
|   +-- Core.Mappings/           Static extension methods: AlarmMappingExtensions (alarm
|   |                            Create/Update DTOs), EntityMappingExtensions (Human, Profile,
|   |                            PoracleWeb-owned tables)
|   +-- Core.Repositories/       SiteSettingRepository, WebhookDelegateRepository,
|   |                            QuickPickDefinitionRepository, QuickPickAppliedStateRepository
|   +-- Core.Services/           Business logic (MonsterService, DashboardService,
|   |                            UserGeofenceService, KojiService, SiteSettingService,
|   |                            WebhookDelegateService, QuickPickService,
|   |                            DiscordNotificationService,
|   |                            PoracleTrackingProxy, PoracleHumanProxy,
|   |                            TestAlertService, GolbatApiProxy,
|   |                            PokemonAvailabilityService, etc.)
|
+-- Data/
|   +-- Data/                    PoracleWebContext (app-owned DB, the only one),
|   |                            Entities/ (incl. UserGeofenceEntity, SiteSettingEntity,
|   |                            WebhookDelegateEntity, QuickPickDefinitionEntity,
|   |                            QuickPickAppliedStateEntity),
|   |                            Configurations/ (EF Core entity type configurations)
|   +-- Data.Scanner/            ScannerContext for optional scanner DB
|
+-- Applications/
|   +-- Web.Api/                 ASP.NET Core host
|   |   +-- Controllers/         20+ API controllers (REST, all under /api/)
|   |   |                        incl. UserGeofenceController, AdminGeofenceController,
|   |   |                        GeofenceFeedController, TestAlertController
|   |   +-- Configuration/       DI registration, JwtSettings, DiscordSettings, KojiSettings, etc.
|   |   +-- Services/            Background services: AvatarCacheService, DtsCacheService,
|   |                            SettingsMigrationStartupService
|   +-- Web.App/
|       +-- ClientApp/           Angular 21 SPA
|           +-- src/app/
|               +-- core/        Guards (auth, admin), services, interceptors, models
|               +-- modules/     Feature modules: auth, dashboard, pokemon, raids,
|               |                quests, invasions, lures, nests, gyms, areas,
|               |                profiles, cleaning, quick-picks, admin, geofences
|               +-- shared/      Reusable components: area-map, pokemon-selector,
|                                template-selector, delivery-preview, location-dialog,
|                                discord-avatar, alarm-info, confirm-dialog,
|                                language-selector, distance-dialog, onboarding,
|                                region-selector, geofence-name-dialog,
|                                geofence-approval-dialog, gym-picker,
|                                active-hours-chip, active-hours-editor-dialog,
|                                location-warning
|                                utils/: geo.utils (point-in-polygon, centroid)
|                                models/: active-hours.models (interfaces, utilities)
|
+-- scripts/
|   +-- setup.sh                 Interactive first-time setup wizard
|   +-- dev.sh                   Dev convenience commands (install, api, app, start, test, lint, build, db:create, db:migrate)
|   +-- docker.sh                Docker convenience commands (build, start, stop, logs, update, clean)
|
+-- Tests/
    +-- Pgan.PoracleWebNet.Tests/  xUnit backend tests (controllers, services, mappings)
```

# File Locations

| What | Path |
|---|---|
| EF Core Entities | `Data/Pgan.PoracleWebNet.Data/Entities/` |
| EF Core DbContext (PoracleWeb) | `Data/Pgan.PoracleWebNet.Data/PoracleWebContext.cs` |
| User Geofence Entity | `Data/Pgan.PoracleWebNet.Data/Entities/UserGeofenceEntity.cs` |
| Site Setting Entity | `Data/Pgan.PoracleWebNet.Data/Entities/SiteSettingEntity.cs` |
| Webhook Delegate Entity | `Data/Pgan.PoracleWebNet.Data/Entities/WebhookDelegateEntity.cs` |
| Quick Pick Definition Entity | `Data/Pgan.PoracleWebNet.Data/Entities/QuickPickDefinitionEntity.cs` |
| Quick Pick Applied State Entity | `Data/Pgan.PoracleWebNet.Data/Entities/QuickPickAppliedStateEntity.cs` |
| Entity Configurations | `Data/Pgan.PoracleWebNet.Data/Configurations/` |
| MariaDb History Repository | `Data/Pgan.PoracleWebNet.Data/MariaDbHistoryRepository.cs` |
| Design-Time Context Factory | `Data/Pgan.PoracleWebNet.Data/PoracleWebContextDesignTimeFactory.cs` |
| EF Core Migrations (PoracleWeb) | `Data/Pgan.PoracleWebNet.Data/Migrations/PoracleWeb/` |
| API Controllers | `Applications/Pgan.PoracleWebNet.Api/Controllers/` |
| Geofence Feed Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/GeofenceFeedController.cs` |
| Admin Geofence Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/AdminGeofenceController.cs` |
| User Geofence Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/UserGeofenceController.cs` |
| Area Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/AreaController.cs` |
| Profile Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/ProfileController.cs` |
| DI Registration | `Applications/Pgan.PoracleWebNet.Api/Configuration/ServiceCollectionExtensions.cs` |
| IJwtService / JwtService | `Applications/Pgan.PoracleWebNet.Api/Configuration/IJwtService.cs`, `JwtService.cs` |
| Settings Classes | `Applications/Pgan.PoracleWebNet.Api/Configuration/` (JwtSettings, DiscordSettings, KojiSettings, etc.) |
| Settings Migration Service | `Applications/Pgan.PoracleWebNet.Api/Services/SettingsMigrationStartupService.cs` |
| Angular App Root | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/` |
| Angular Routes | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/app.routes.ts` |
| Angular Services | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/` |
| Angular Guards | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/guards/` |
| Shared Components | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/` |
| Feature Modules | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/` |
| Geofences Module | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/geofences/` |
| Admin Geofence Submissions | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/admin/geofence-submissions/` |
| Region Selector Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/region-selector/` |
| Geofence Name Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/geofence-name-dialog/` |
| Geofence Approval Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/geofence-approval-dialog/` |
| Gym Picker Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/gym-picker/` |
| Scanner Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/scanner.service.ts` |
| Geo Utilities | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/geo.utils.ts` |
| Alarm Mapping Extensions | `Core/Pgan.PoracleWebNet.Core.Mappings/AlarmMappingExtensions.cs` |
| Entity Mapping Extensions | `Core/Pgan.PoracleWebNet.Core.Mappings/EntityMappingExtensions.cs` |
| IUpstreamFeatureFlagService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUpstreamFeatureFlagService.cs` |
| UpstreamFeatureFlagService | `Core/Pgan.PoracleWebNet.Core.Services/UpstreamFeatureFlagService.cs` |
| PoracleDisabledHookMap | `Core/Pgan.PoracleWebNet.Core.Models/PoracleDisabledHookMap.cs` |
| Pokemon type id-to-name table | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/pokemon-types.ts` |
| Basemap catalogue | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/basemaps.ts` |
| BasemapService | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/basemap.service.ts` |
| IPoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleTrackingProxy.cs` |
| IPoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleHumanProxy.cs` |
| PoracleTrackingProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleTrackingProxy.cs` |
| PoracleHumanProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleHumanProxy.cs` |
| PoracleJsonHelper | `Core/Pgan.PoracleWebNet.Core.Services/PoracleJsonHelper.cs` |
| IPoracleV2SchemaService (what the server's own /openapi.json declares) | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleV2SchemaService.cs` |
| PoracleV2SchemaService | `Core/Pgan.PoracleWebNet.Core.Services/PoracleV2SchemaService.cs` |
| PoracleV2Capabilities | `Core/Pgan.PoracleWebNet.Core.Models/PoracleV2Capabilities.cs` |
| TrackingV2Translator (v1 row -> /api/v2 body) | `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Translator.cs` |
| TrackingV2Replacement (the v2 branch each UpdateAsync takes) | `Core/Pgan.PoracleWebNet.Core.Services/TrackingV2Replacement.cs` |
| PoracleProblemDetails (RFC 9457 + v1 errors) | `Core/Pgan.PoracleWebNet.Core.Services/PoracleProblemDetails.cs` |
| Repositories (non-alarm) | `Core/Pgan.PoracleWebNet.Core.Repositories/` |
| SiteSettingRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/SiteSettingRepository.cs` |
| WebhookDelegateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/WebhookDelegateRepository.cs` |
| QuickPickDefinitionRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickDefinitionRepository.cs` |
| QuickPickAppliedStateRepository | `Core/Pgan.PoracleWebNet.Core.Repositories/QuickPickAppliedStateRepository.cs` |
| IUserAreaWriter | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaWriter.cs` |
| IUserAreaPolicy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IUserAreaPolicy.cs` |
| UserAreaWriter | `Core/Pgan.PoracleWebNet.Core.Services/UserAreaWriter.cs` |
| UserAreaPolicy | `Core/Pgan.PoracleWebNet.Core.Services/UserAreaPolicy.cs` |
| OverrideScope | `Core/Pgan.PoracleWebNet.Core.Services/OverrideScope.cs` |
| Service Layer | `Core/Pgan.PoracleWebNet.Core.Services/` |
| UserGeofenceService | `Core/Pgan.PoracleWebNet.Core.Services/UserGeofenceService.cs` |
| SiteSettingService | `Core/Pgan.PoracleWebNet.Core.Services/SiteSettingService.cs` |
| WebhookDelegateService | `Core/Pgan.PoracleWebNet.Core.Services/WebhookDelegateService.cs` |
| QuickPickService | `Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs` |
| KojiService | `Core/Pgan.PoracleWebNet.Core.Services/KojiService.cs` |
| DiscordNotificationService | `Core/Pgan.PoracleWebNet.Core.Services/DiscordNotificationService.cs` |
| GeoMath (area/centroid/containment) | `Core/Pgan.PoracleWebNet.Core.Services/GeoMath.cs` |
| GeofenceSubmissionPost Model | `Core/Pgan.PoracleWebNet.Core.Models/GeofenceSubmissionPost.cs` |
| IScannerService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IScannerService.cs` |
| ScannerService | `Core/Pgan.PoracleWebNet.Core.Services/ScannerService.cs` |
| GymSearchResult Model | `Core/Pgan.PoracleWebNet.Core.Models/GymSearchResult.cs` |
| Test Alert Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/TestAlertController.cs` |
| ITestAlertService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/ITestAlertService.cs` |
| Rule Repair Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/AdminRuleRepairController.cs` |
| RuleRepairJob / RuleRepairClassifier | `Core/Pgan.PoracleWebNet.Core.Services/RuleRepairJob.cs`, `RuleRepairClassifier.cs` |
| Rule Repair models | `Core/Pgan.PoracleWebNet.Core.Models/RuleRepair.cs` |
| Rule Repair card / service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/admin/rule-repair-card/`, `core/services/rule-repair.service.ts` |
| Mute Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/MuteController.cs` |
| IPoracleMuteProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPoracleMuteProxy.cs` |
| PoracleMuteProxy | `Core/Pgan.PoracleWebNet.Core.Services/PoracleMuteProxy.cs` |
| Mute / MuteScopes Models | `Core/Pgan.PoracleWebNet.Core.Models/Mute.cs`, `MuteScopes.cs` |
| Mute Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/mute.service.ts` |
| Quiet Chip / Sheet / List Sheet | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/quiet-chip/`, `quiet-sheet/`, `quiet-list-sheet/` |
| TestAlertService | `Core/Pgan.PoracleWebNet.Core.Services/TestAlertService.cs` |
| TestAlertRequest Model | `Core/Pgan.PoracleWebNet.Core.Models/TestAlertRequest.cs` |
| Test Alert Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/test-alert.service.ts` |
| Scanner Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/ScannerController.cs` |
| Scanner DB Context | `Data/Pgan.PoracleWebNet.Data.Scanner/` |
| GolbatSettings | `Applications/Pgan.PoracleWebNet.Api/Configuration/GolbatSettings.cs` |
| IGolbatApiProxy | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IGolbatApiProxy.cs` |
| GolbatApiProxy | `Core/Pgan.PoracleWebNet.Core.Services/GolbatApiProxy.cs` |
| IPokemonAvailabilityService | `Core/Pgan.PoracleWebNet.Core.Abstractions/Services/IPokemonAvailabilityService.cs` |
| PokemonAvailabilityService | `Core/Pgan.PoracleWebNet.Core.Services/PokemonAvailabilityService.cs` |
| PokemonAvailability Controller | `Applications/Pgan.PoracleWebNet.Api/Controllers/PokemonAvailabilityController.cs` |
| Pokemon Availability Service (frontend) | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/services/pokemon-availability.service.ts` |
| Active Hours Models | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/core/models/active-hours.models.ts` |
| Active Hours Chip Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/active-hours-chip/` |
| Active Hours Editor Dialog | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/active-hours-editor-dialog/` |
| Location Warning Component | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/components/location-warning/` |
| Active Hours Utilities | `Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/shared/utils/active-hours.utils.spec.ts` |
| Abstractions | `Core/Pgan.PoracleWebNet.Core.Abstractions/` |
| DataProtection Configuration Tests | `Tests/Pgan.PoracleWebNet.Tests/Configuration/DataProtectionConfigurationTests.cs` |
| Backend Tests | `Tests/Pgan.PoracleWebNet.Tests/` |
| CI Workflows | `.github/workflows/` (ci.yml, docker-publish.yml) |
| Scripts | `scripts/` (`setup.sh`, `dev.sh`, `docker.sh`) |
| Docker Config | `Dockerfile`, `docker-compose.yml.example` (copy to `docker-compose.yml`), `.env.example` |
