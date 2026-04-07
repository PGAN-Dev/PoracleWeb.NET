# Alarm Management

PoracleWeb provides a browser-based UI for managing Poracle notification filters. Users create alarms that tell Poracle which Pokemon, Raids, Quests, and other events to send as DM notifications.

All alarm CRUD operations are proxied through the PoracleNG REST API. PoracleNG handles field defaults, deduplication, and immediate state reload. See [PoracleNG API Proxy](../architecture/poracleng-proxy.md) for technical details.

## Alarm types

| Type | Description |
|---|---|
| **Pokemon** | Filter by species, IV, CP, level, PVP rank, gender, size |
| **Raids** | Filter by raid boss, tier, move, evolution, EX eligibility, specific gym, RSVP changes |
| **Eggs** | Filter by egg tier, EX eligibility, specific gym, RSVP changes |
| **Quests** | Filter by reward type and Pokemon |
| **Invasions** | Filter by grunt type and shadow Pokemon |
| **Lures** | Filter by lure type |
| **Nests** | Filter by nesting Pokemon species |
| **Gyms** | Filter by gym team changes, battle activity, specific gym |
| **Fort Changes** | Filter by fort type (pokestop/gym), change types (name, location, image, removal, new) |
| **Max Battles** | Filter by battle level (1-5 Dynamax, 7/8 Gigantamax), specific Pokemon, Gigantamax-only toggle |

## Creating alarms

Each alarm type has a dedicated page accessible from the sidebar navigation. The creation flow:

1. Click the **+** (add) button
2. Select the Pokemon/raid/quest target using the selector dialog
3. Configure filter options (IV range, CP range, level, etc.)
4. Set a **distance** — how far from your location to receive alerts (in meters)
5. Optionally select a **template** for notification formatting
6. Save the alarm

## Alarm cards

Alarms are displayed as a card grid. Each card shows:

- Pokemon sprite or raid/quest icon
- **Filter pills** — Quick-glance badges showing active filters (IV, CP, Level, PVP, Gender, Size)
- Distance setting
- Template name
- **Targeted gym name** — Gym, Raid, and Egg alarm cards display the name of the targeted gym when a specific gym is selected (via the gym picker)
- Edit/delete actions

## Bulk operations

Each alarm list page has a **select mode** toggle (checklist icon in the toolbar):

1. Toggle select mode on
2. Check individual alarms or use **Select All**
3. The bulk toolbar appears with available actions:
    - **Update Distance** — Set a new distance for all selected alarms
    - **Delete** — Remove all selected alarms

!!! tip "Bulk distance uses the PoracleNG API"
    Bulk distance updates fetch all matching alarms from PoracleNG, modify the distance field, and POST them back. This ensures PoracleNG validates the data and triggers a state reload.

## Profiles

Users can maintain multiple alarm profiles. Only one profile is active at a time.

- The **Profiles** page shows all alarms across all profiles in a unified overview
- Switch profiles, edit, duplicate, delete, export, and import — all from one page
- Each alarm is associated with a `profile_no`
- The active profile is tracked by `humans.current_profile_no`

### Cross-Profile Overview

The Profiles page uses PoracleNG's `GET /api/tracking/allProfiles/{id}?includeDescriptions=true` endpoint to fetch all alarms across all profiles in a single call. Alarms are grouped by profile (expandable accordion panels) and by type within each profile.

### Duplicating a profile

The **Duplicate** button on each profile panel creates a new profile with all alarms copied from the source profile:

1. Click the :material-content-copy: **Duplicate** button on any profile panel
2. Enter a name for the new profile (pre-filled as `"<source name> (Copy)"`)
3. Click **Duplicate**

The new profile inherits the source profile's areas, location, and all alarm filters. If the alarm copy step fails, the empty profile is automatically rolled back (deleted) so the user never ends up with a shell profile.

!!! tip "Duplicate vs. Create"
    Use **Duplicate** when you want to start with an existing set of alarms and tweak them. Use **Create** when you want a blank profile.

### Profile Backup & Restore

- **Export**: Per-profile JSON backup containing all alarm filters, stripped of internal fields (`uid`, `id`, `profile_no`). File format: `{ version: 1, exportedAt, profileName, alarms: { pokemon: [...], raid: [...], ... } }`
- **Import**: Upload a backup file to create a new profile with all alarms restored. Profile names are auto-deduplicated if a matching name already exists.

### Profile Name Uniqueness

Profile names must be unique per user. Validated client-side in all entry points (add, edit, duplicate, import dialogs). Server-side auto-deduplication appends a numeric suffix as a fallback.

## Pokemon alarm filters

### Size filter

The size filter uses special sentinel values:

- **`size = -1`** — No filter (ALL sizes). This is the default.
- **`size = 1`** through **`size = 5`** — Specific sizes: 1 = XXS, 2 = XS, 3 = Normal, 4 = XL, 5 = XXL.
- **`max_size = 5`** — Default upper bound.

When a user selects a specific size, both `size` and `max_size` are set to the same value, creating an exact match. For example, selecting XXL sets `size = 5, max_size = 5`.

### Level range

The default maximum level is **55** (not 40 or 50), matching Poracle's support for shadow/purified/best-buddy boosted levels.

## Raid alarm filters

Raid alarms support these fields beyond the basic tier/boss selection:

| Field | Default | Description |
|---|---|---|
| `team` | `4` (any team) | Gym team controlling the raid |
| `move` | `9000` (any move) | Filter by raid boss move |
| `evolution` | `9000` (any) | Filter by evolution type (e.g., Mega, Primal) |
| `exclusive` | `false` | EX/exclusive raid flag |
| `gymId` | `null` (all gyms) | Track a specific gym by ID (set via gym picker) |
| `rsvpChanges` | `false` | Receive RSVP change notifications |

## Egg alarm filters

Egg alarms support:

| Field | Default | Description |
|---|---|---|
| `team` | `4` (any team) | Gym team controlling the egg |
| `exclusive` | `false` | EX/exclusive egg flag |
| `gymId` | `null` (all gyms) | Track a specific gym by ID (set via gym picker) |
| `rsvpChanges` | `false` | Receive RSVP change notifications |

## Gym alarm filters

Gym alarms support:

| Field | Default | Description |
|---|---|---|
| `team` | `4` (any team) | Gym team to track |
| `battleChanges` | `false` | Notify on battle activity changes at the gym |
| `gymId` | `null` (all gyms) | Track a specific gym by ID (set via gym picker) |

## Fort change alarm filters

Fort change alarms track changes to pokestops and gyms as points of interest (not activity at them).

| Field | Default | Description |
|---|---|---|
| `fort_type` | `"everything"` | Fort type to track: `pokestop`, `gym`, or `everything` |
| `include_empty` | `0` (false) | Include forts with no name |
| `change_types` | `[]` (all) | JSON array of change types to monitor: `name`, `location`, `image_url`, `removal`, `new` |

Fort change alarms are proxied through PoracleNG using tracking type `"fort"`. The API endpoints follow the standard alarm CRUD pattern at `/api/fort-changes`.

## Max Battle alarm filters

Max Battle (Dynamax) alarms track battles at Power Spot stations. There are two tracking modes:

- **By Level** — Select battle tiers to track any Pokemon at those levels. One alarm per level.
- **By Pokemon** — Select specific Pokemon to track across all Max Battle levels.

| Field | Default | Description |
|---|---|---|
| `pokemon_id` | `9000` (any) | Pokemon to track. `9000` = level-based tracking (any Pokemon). |
| `level` | `9000` (any) | Battle level. Only meaningful when `pokemon_id = 9000`. |
| `gmax` | `0` (any) | Gigantamax filter. `0` = matches all battles, `1` = Gigantamax only. |
| `form` | `0` (any) | Pokemon form filter. |

### Battle levels

Max Battle levels follow the PoracleNG `util.json` definitions:

| Level | Label | Type |
|---|---|---|
| 1 | 1 Star Max Battle | Dynamax |
| 2 | 2 Star Max Battle | Dynamax |
| 3 | 3 Star Max Battle | Dynamax |
| 4 | 4 Star Max Battle | Dynamax |
| 5 | Legendary Max Battle | Dynamax |
| 7 | Gigantamax Battle | Gigantamax |
| 8 | Legendary Gigantamax Battle | Gigantamax |

!!! note "No level 6"
    There is no level 6 in PoracleNG's max battle system. Levels 7 and 8 are Gigantamax battles where `gmax` is automatically derived (level > 6 = gmax).

### Insert-only API behavior

Unlike other alarm types, the PoracleNG maxbattle API handler has **no diff/dedup logic** — every POST creates new rows. Updates use a delete-then-create pattern: delete the old alarm by UID, then insert the replacement. This is handled transparently by `MaxBattleService.UpdateAsync()`.

### Scanner-based Pokemon filter

When the scanner database is configured, the "By Pokemon" tab queries the `station` table for distinct `battle_pokemon_id` values. This limits the Pokemon selector to species that have actually appeared in Max Battles. If the scanner DB is not configured, all Pokemon are shown.

!!! warning "GymCreate.Team default"
    `GymCreate.Team` must default to `4` (any team), matching Raid and Egg defaults. A C# `int` defaults to `0`, which maps to "Neutral only" in Poracle, causing new gym alarms to silently filter out all non-Neutral gyms.

## Gym picker

The **gym picker** is a shared component (`app-gym-picker`) that allows users to optionally target a specific gym when creating or editing **Gym**, **Raid**, or **Egg** alarms. When a gym is selected, alerts only fire for events at that particular gym.

### How it works

1. The picker displays a search field labeled "Search for a gym (optional)".
2. As the user types (minimum 2 characters), the component debounces input (300ms) and queries the scanner database via `GET /api/scanner/gyms?search=<term>&limit=20`.
3. Results appear in an autocomplete dropdown. Each option shows:
    - **Gym photo thumbnail** (from the scanner DB), or a team icon fallback if no photo is available
    - **Gym name** (or gym ID if the name is not set)
    - **Area name** — resolved by checking which Koji geofence polygon contains the gym's coordinates (via point-in-polygon), or lat/lon coordinates if no area matches
4. Selecting a gym sets the `gymId` on the alarm. A compact chip displays the selected gym's photo, name, and area with a clear button to remove the selection.
5. In **edit mode**, the picker loads the existing gym's details from `GET /api/scanner/gyms/{id}` to display the name and photo.

### Requirements

- The **scanner database** must be configured (`ConnectionStrings:ScannerDb`). If not configured, the `IScannerService` is not registered and the search endpoints return empty results.
- The **Koji service** is optional. When available, it enriches results with area names by checking gym coordinates against Koji geofence polygons.

### Backend

- `ScannerController` exposes `GET /api/scanner/gyms` (search) and `GET /api/scanner/gyms/{id}` (lookup by ID).
- `IScannerService.SearchGymsAsync()` queries the scanner DB's gym table by name (LIKE match), returning `GymSearchResult` with `Id`, `Name`, `Url`, `Lat`, `Lon`, `TeamId`, and `Area`.
- Area enrichment runs server-side: for each gym result, the controller iterates Koji admin geofences and assigns the first matching fence name.

## Invasion alarm filters

Invasion alarms filter by grunt type. The `grunt_type` value is **automatically lowercased** on create because Poracle uses case-sensitive matching for grunt types.

## Default values

Comprehensive table of all monster (Pokemon) alarm defaults, matching the PHP PoracleWeb defaults:

| Field | Default | Description |
|---|---|---|
| `min_iv` | `0` | Minimum IV percentage |
| `max_iv` | `100` | Maximum IV percentage |
| `min_cp` | `0` | Minimum CP |
| `max_cp` | `9000` | Maximum CP |
| `min_level` | `0` | Minimum level |
| `max_level` | `55` | Maximum level |
| `min_weight` | `0` | Minimum weight |
| `max_weight` | `9000000` | Maximum weight |
| `atk` | `0` | Minimum attack IV |
| `def` | `0` | Minimum defense IV |
| `sta` | `0` | Minimum stamina IV |
| `max_atk` | `15` | Maximum attack IV |
| `max_def` | `15` | Maximum defense IV |
| `max_sta` | `15` | Maximum stamina IV |
| `pvp_ranking_best` | `0` | Best PVP ranking position |
| `pvp_ranking_worst` | `4096` | Worst PVP ranking position |
| `gender` | `0` | Gender filter (0 = any) |
| `size` | `-1` | Size filter (-1 = no filter / all sizes) |
| `max_size` | `5` | Maximum size upper bound |

Raid-specific defaults:

| Field | Default |
|---|---|
| `team` | `4` (any team) |
| `move` | `9000` (any move) |
| `evolution` | `9000` (any evolution) |

Egg-specific defaults:

| Field | Default |
|---|---|
| `team` | `4` (any team) |

Gym-specific defaults:

| Field | Default |
|---|---|
| `team` | `4` (any team) |

Max Battle-specific defaults:

| Field | Default |
|---|---|
| `pokemon_id` | `9000` (any Pokemon / level-based) |
| `level` | `9000` (any level) |
| `gmax` | `0` (any — not Gigantamax-only) |
| `move` | `9000` (any move) |
| `evolution` | `9000` (any — unused placeholder) |

## Quick Picks

Admins can define **Quick Pick** templates — pre-configured alarm sets that users can apply with one click. Useful for onboarding new users or sharing recommended configurations.
