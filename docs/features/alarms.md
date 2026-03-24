# Alarm Management

PoracleWeb provides a browser-based UI for managing Poracle notification filters. Users create alarms that tell Poracle which Pokemon, Raids, Quests, and other events to send as DM notifications.

## Alarm types

| Type | Description |
|---|---|
| **Pokemon** | Filter by species, IV, CP, level, PVP rank, gender, size |
| **Raids** | Filter by raid boss, tier, EX eligibility |
| **Quests** | Filter by reward type and Pokemon |
| **Invasions** | Filter by grunt type and shadow Pokemon |
| **Lures** | Filter by lure type |
| **Nests** | Filter by nesting Pokemon species |
| **Gyms** | Filter by gym team changes |

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
- Edit/delete actions

## Bulk operations

Each alarm list page has a **select mode** toggle (checklist icon in the toolbar):

1. Toggle select mode on
2. Check individual alarms or use **Select All**
3. The bulk toolbar appears with available actions:
    - **Update Distance** — Set a new distance for all selected alarms
    - **Delete** — Remove all selected alarms

!!! tip "Bulk distance uses a dedicated endpoint"
    Bulk distance updates use `PUT /distance/bulk` which does a targeted SQL update without touching other alarm fields. This is safer than updating each alarm individually.

## Profiles

Users can maintain multiple alarm profiles. Only one profile is active at a time.

- Switch profiles from the **Profiles** page or the user menu
- Each alarm is associated with a `profile_no`
- The active profile is tracked by `humans.current_profile_no`

## Quick Picks

Admins can define **Quick Pick** templates — pre-configured alarm sets that users can apply with one click. Useful for onboarding new users or sharing recommended configurations.
