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

## UI patterns

### Alarm lists
Card grid with filter pills showing IV/CP/Level/PVP/Gender at a glance.

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

### Gym picker

`GymPickerComponent` (`shared/components/gym-picker/`) is a standalone autocomplete for selecting a gym from the scanner database. It wraps a Material autocomplete input with debounced search (300ms, minimum 2 characters). Each option row displays the gym photo thumbnail, name, and area name. The component exposes a two-way `gymId` model binding so parent dialogs can read/write the selected gym ID directly.

In edit mode, when the component initializes with an existing `gymId`, an `effect()` calls `ScannerService.getGymById` to load and display the gym details. A `searchSubject` piped through `debounceTime` / `distinctUntilChanged` / `switchMap` drives the autocomplete options. Subscription cleanup uses `takeUntilDestroyed`.

Integrated into gym-add-dialog, gym-edit-dialog, raid-add-dialog, and raid-edit-dialog.

### Gym list cards

Gym alarm list cards resolve and display targeted gym names. When alarms have a `gym_id`, the component uses `ScannerService.getGymById` with `forkJoin` to batch-lookup gym details, showing the gym name on each card instead of the raw ID.

### Keyboard shortcuts

| Key | Action |
|---|---|
| ++question++ | Help |
| ++bracket-left++ | Collapse sidebar |
| ++bracket-right++ | Expand sidebar |
