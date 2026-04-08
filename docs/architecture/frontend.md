# Frontend Patterns

## Angular conventions

- All components are **standalone** (no NgModules)
- Uses `inject()` function instead of constructor injection
- Uses Angular signals for reactive state where applicable
- Lazy-loaded routes in `app.routes.ts`
- Services in `core/services/` use `HttpClient` to call the .NET API

## Project structure

```
src/app/
├── core/
│   ├── guards/          Auth guard, admin guard
│   ├── services/        HTTP services for each API resource
│   ├── interceptors/    JWT token interceptor
│   └── models/          TypeScript interfaces
├── modules/
│   ├── auth/            Login page
│   ├── dashboard/       Dashboard with onboarding
│   ├── pokemon/         Pokemon alarm management
│   ├── raids/           Raid alarm management
│   ├── quests/          Quest alarm management
│   ├── invasions/       Invasion alarm management
│   ├── lures/           Lure alarm management
│   ├── nests/           Nest alarm management
│   ├── gyms/            Gym alarm management
│   ├── fort-changes/    Fort change alarm management
│   ├── max-battles/     Max Battle (Dynamax) alarm management
│   ├── areas/           Area selection with map
│   ├── geofences/       Custom geofence drawing
│   ├── profiles/        Profile management
│   ├── cleaning/        Alarm cleanup tools
│   ├── quick-picks/     Quick pick alarm templates
│   └── admin/           Admin panel (users, servers, geofences)
└── shared/
    ├── components/      Reusable UI components
    └── utils/           Utility functions (geo.utils, etc.)
```

## Services

### ScannerService

`ScannerService` (`core/services/scanner.service.ts`) provides access to the optional scanner database for gym lookups. Both methods use `catchError` for graceful degradation when the scanner DB is unavailable (returns `of(null)` or `of([])`).

- `searchGyms(search, limit)` — Searches gyms by name. Returns an empty array if the search term is less than 2 characters.
- `getGymById(id)` — Fetches a single gym by ID. Returns `null` on error.

The `GymSearchResult` interface defines the shape: `id`, `name`, `url`, `lat`, `lon`, `teamId`, and `area`.

### TestAlertService

`TestAlertService` (`core/services/test-alert.service.ts`) manages test alert requests from alarm list cards. It tracks per-UID cooldowns (15-second TTL via a `Map`) and deduplicates in-flight requests to prevent duplicate API calls. After sending, it displays success/error/cooldown feedback via Material snackbar. The test button appears in `mat-card-actions` on all alarm card types (Pokemon, Raids, Eggs, Quests, Invasions, Lures, Nests, Gyms, Fort Changes, Max Battles).

### ProfileService — active hours

`ProfileService` (`core/services/profile.service.ts`) parses the `activeHours` field from the API response in `getAll()`, mapping the snake_case `active_hours` JSON string to a typed `ActiveHoursEntry[]` on each profile model.

- `updateActiveHours(profileNo, entries)` — sends the active hours array to the API for the given profile

The `active-hours.models.ts` file (`core/models/`) defines the `ActiveHoursEntry` interface and utility functions for working with time-window rules (serialization, display formatting, validation).

### PokemonAvailabilityService

`PokemonAvailabilityService` (`core/services/pokemon-availability.service.ts`) provides Pokemon spawn availability data from the Golbat scanner API. It is a `providedIn: 'root'` singleton.

- `enabled` signal — `true` when Golbat is configured on the backend
- `availableIds` signal — `Set<number>` of currently spawning Pokemon IDs for O(1) lookups
- `isAvailable(id)` — returns whether a Pokemon is currently spawning
- `load()` — idempotent initial fetch + 5-minute auto-refresh via `setInterval`
- Graceful degradation: preserves `enabled` state and stale data on refresh errors

The `PokemonSelectorComponent` injects this service and calls `load()` in `ngOnInit()`. When `enabled()` is true, it renders:

- A "Live > Spawning" filter toggle chip (with animated pulse dot)
- Green availability dots on autocomplete options and tile grid items
- Available-first sorting when any filter is active

## UI patterns

### Alarm lists
Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance. All alarm types (including Fort Changes and Max Battles) follow this same card grid pattern with type-specific filter pills.

### Bulk operations
Select mode toggle (checklist icon) on each alarm list. Bulk toolbar provides Select All, Update Distance, and Delete actions.

### Loading states
Animated skeleton card placeholders on Pokemon, Raids, and Quests pages.

### Animations
Grid items fade in with 30ms stagger delay.

### Theming

**Accent themes** — Toolbar gradient, sidenav active link, and UI accent colors are customizable via the user menu. Colors are applied as CSS custom properties on `document.body.style` to work across Angular's view encapsulation.

**Dark/light mode** — CSS variables bridge Material tokens to component styles. Theme stored in `localStorage('poracle-theme')`.

### Onboarding wizard
Shows on the dashboard for new users until explicitly dismissed. Detects existing location/areas/alarms and marks steps as complete. Route-based actions (Choose Areas, Add Alarm) hide the overlay temporarily without setting the localStorage completion flag.

### Active hours components

`ActiveHoursChipComponent` (`shared/components/active-hours-chip/`) renders a compact read-only summary of a profile's active hours schedule as a Material chip. Displayed inline on `ProfileOverviewComponent` cards.

`ActiveHoursEditorDialogComponent` (`shared/components/active-hours-editor-dialog/`) provides a full editing UI for active hours rules. Opened from the profile overview when the user clicks the active hours chip or the "Set active hours" action. Validates entries client-side (day 1--7, hours 0--23, mins 0--59, max 28 entries) before submitting.

`LocationWarningComponent` (`shared/components/location-warning/`) displays a contextual warning when the user's location is not set, since active hours depend on the user's timezone derived from their location.

`ProfileOverviewComponent` integrates active hours display and editing — each profile card shows the `ActiveHoursChipComponent` and opens the editor dialog for modifications.

!!! note "`ProfileListComponent` removed"
    The unused `ProfileListComponent` has been removed. Profile management is handled entirely by `ProfileOverviewComponent`.

### Gym picker

`GymPickerComponent` (`shared/components/gym-picker/`) is a standalone autocomplete for selecting a gym from the scanner database. It wraps a Material autocomplete input with debounced search (300ms, minimum 2 characters). Each option row displays the gym photo thumbnail, name, and area name. The component exposes a two-way `gymId` model binding so parent dialogs can read/write the selected gym ID directly.

In edit mode, when the component initializes with an existing `gymId`, an `effect()` calls `ScannerService.getGymById` to load and display the gym details. A `searchSubject` piped through `debounceTime` / `distinctUntilChanged` / `switchMap` drives the autocomplete options. Subscription cleanup uses `takeUntilDestroyed`.

Integrated into gym-add-dialog, gym-edit-dialog, raid-add-dialog, and raid-edit-dialog.

### Gym list cards

Gym alarm list cards resolve and display targeted gym names. When alarms have a `gym_id`, the component uses `ScannerService.getGymById` with `forkJoin` to batch-lookup gym details, showing the gym name on each card instead of the raw ID.

### GeoJSON import/export

The geofences module includes GeoJSON import and export dialogs for interoperability with external mapping tools. Users can export their custom geofences as GeoJSON `FeatureCollection` files and import geofences from GeoJSON files. The import dialog validates the GeoJSON structure and previews polygons on a Leaflet map before confirming the import. Admin geofence management also supports GeoJSON export for bulk operations.

### Keyboard shortcuts

| Key | Action |
|---|---|
| ++question++ | Help |
| ++bracket-left++ | Collapse sidebar |
| ++bracket-right++ | Expand sidebar |
