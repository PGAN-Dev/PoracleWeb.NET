# Site Settings

Site settings are admin-configurable runtime settings stored in the `poracle_web.site_settings` database table. Unlike [appsettings.json configuration](reference.md) which requires a restart, site settings are changed at runtime via the **Admin > Settings** page and take effect immediately.

!!! info "First-time setup"
    On first startup after upgrade, the `SettingsMigrationStartupService` automatically migrates any existing data from the deprecated `pweb_settings` key-value store to the structured `site_settings` table. This is idempotent and safe to run multiple times.

## How it works

- Settings are stored in the `poracle_web.site_settings` table with columns: `category`, `key`, `value`, and `value_type`.
- The admin panel at **Admin > Settings** provides a grouped UI for editing all settings.
- Changes take effect immediately — no app restart is needed.
- Boolean settings use `"True"` / `"False"` string values.
- Some settings have conditional visibility (e.g., `allowed_role_ids` only appears when `enable_roles` is enabled).

---

## Branding

Customize the appearance and navigation of your PoracleWeb instance.

| Key | Label | Type | Description |
|---|---|---|---|
| `custom_title` | Site Title | string | Name shown in the browser tab and page header. This is the only setting visible publicly (on the login page without authentication). |
| `header_logo_url` | Header Logo URL | url | URL for a custom logo image in the header (replaces the default Pokeball). Leave empty for the default logo. |
| `hide_header_logo` | Hide Header Logo | boolean | Hide the logo from the header entirely. |
| `custom_page_name` | Nav Link Label | string | Label for a custom navigation link in the sidebar (e.g., "Back To Map"). Leave empty to hide. |
| `custom_page_url` | Nav Link URL | url | URL the custom nav link points to. |
| `custom_page_icon` | Nav Link Icon | string | FontAwesome class for the nav link icon (e.g., `fas fa-map`). |

---

## Alarm Types

Control which alarm categories are available to users. Disabling a type hides it from the sidebar navigation and prevents access.

| Key | Label | Type | Description |
|---|---|---|---|
| `disable_mons` | Disable Pokémon | boolean | Hide Pokémon alarm management from all users. |
| `disable_raids` | Disable Raids | boolean | Hide raid alarm management from all users. |
| `disable_quests` | Disable Quests | boolean | Hide quest alarm management from all users. |
| `disable_invasions` | Disable Invasions | boolean | Hide invasion alarm management from all users. |
| `disable_lures` | Disable Lures | boolean | Hide lure alarm management from all users. |
| `disable_nests` | Disable Nests | boolean | Hide nest alarm management from all users. |
| `disable_gyms` | Disable Gyms | boolean | Hide gym alarm management from all users. |
| `disable_fort_changes` | Disable Fort Changes | boolean | Hide fort change alarm management from all users. |

---

## Features

Toggle user-facing features on or off.

| Key | Label | Type | Description |
|---|---|---|---|
| `disable_areas` | Disable Areas | boolean | Prevent users from managing their area subscriptions. |
| `disable_profiles` | Disable Profiles | boolean | Prevent users from creating and switching alarm profiles. |
| `disable_location` | Disable Location | boolean | Prevent users from setting a home location. |
| `disable_nominatim` | Disable Geocoding | boolean | Disable Nominatim address search for location picking. |
| `disable_geomap` | Disable Map View | boolean | Hide the interactive geofence map entirely. |
| `disable_geomap_select` | Disable Map Area Selection | boolean | Prevent users from selecting areas by clicking the map. Independent of `disable_geomap`. |
| `enable_templates` | Enable Templates | boolean | Allow users to choose notification message templates. |

---

## Administration

Access control and language restrictions.

| Key | Label | Type | Description |
|---|---|---|---|
| `enable_roles` | Enable Role-Based Access | boolean | Only allow users with specific Discord roles to log in. Requires `Discord:BotToken` and `Discord:GuildId` in [appsettings](reference.md). |
| `allowed_role_ids` | Allowed Role IDs | csv | Comma-separated Discord role IDs that grant access (e.g., `123456789,987654321`). Leave empty to allow all. Only visible when `enable_roles` is enabled. |
| `allowed_languages` | Allowed Languages | csv | Comma-separated language codes users can select (e.g., `en,de,fr`). Leave empty to show all available languages. |

!!! warning "Role-based access prerequisites"
    Role-based access requires `Discord:BotToken` and `Discord:GuildId` to be configured in appsettings. Without these, role checks cannot be performed and the setting has no effect.

---

## Commands

Poracle bot commands shown in help text and the onboarding wizard.

| Key | Label | Type | Description |
|---|---|---|---|
| `register_command` | Register Command | string | The Poracle bot command users run to register (e.g., `$!register`). |
| `location_command` | Location Command | string | The Poracle bot command users run to set their location. |

---

## Telegram

Configure Telegram authentication alongside or instead of Discord.

| Key | Label | Type | Description |
|---|---|---|---|
| `enable_telegram` | Enable Telegram | boolean | Allow users to log in and manage alarms via Telegram. |
| `telegram_bot` | Bot Username | string | Telegram bot username (without the `@` prefix). |

!!! note "Backend configuration also required"
    Enabling Telegram in site settings also requires `Telegram:BotToken` and `Telegram:BotUsername` to be set in [appsettings](reference.md).

---

## Maps & Assets

Configure the map tile provider used for static map images.

| Key | Label | Type | Description |
|---|---|---|---|
| `provider_url` | Map Tile URL | url | URL template for the map tile provider. Uses standard `{z}/{x}/{y}` placeholders. Example: `https://tile.openstreetmap.org/{z}/{x}/{y}.png` |

---

## Analytics & Links

Optional analytics tracking and donation links.

| Key | Label | Type | Description |
|---|---|---|---|
| `gAnalyticsId` | Google Analytics ID | string | GA4 measurement ID (e.g., `G-XXXXXXXXXX`). Leave blank to disable analytics. |
| `patreonUrl` | Patreon URL | url | Link to your Patreon page, shown in the UI when set. |
| `paypalUrl` | PayPal URL | url | Link to your PayPal donation page, shown in the UI when set. |

---

## Debug

Development and troubleshooting settings.

| Key | Label | Type | Description |
|---|---|---|---|
| `site_is_https` | Site Is HTTPS | boolean | Mark the site as running over HTTPS. Affects cookie security flags (`Secure`, `SameSite`). |
| `debug` | Debug Mode | boolean | Enable verbose debug logging. Not recommended in production. |

!!! warning
    Enabling debug mode in production can expose sensitive information in logs and degrade performance.

---

## Icon Repository

Icon URLs are configured via the visual **Icon Repository** picker in the admin settings UI. The picker sets all icon URLs at once from a preset repository. You can also set them individually.

| Key | Type | Description |
|---|---|---|
| `uicons_pkmn` | url | Base URL for Pokémon icon images. |
| `uicons_gym` | url | Base URL for gym icon images. |
| `uicons_raid` | url | Base URL for raid icon images. |
| `uicons_reward` | url | Base URL for reward/quest icon images. |
| `uicons_item` | url | Base URL for item icon images. |
| `uicons_type` | url | Base URL for type icon images. |

Built-in icon repositories include:

- **Whitewillem (Ingame)** — In-game style assets
- **Nileplumb (Home)** — Pokémon HOME style
- **Nileplumb (Shuffle)** — Pokémon Shuffle style
- **Jms412 (Home)** — Alternative HOME style
- **Jms412 (Pokedex)** — Pokédex style

All repositories use the [UICONS](https://github.com/UIcons/UIcons) standard format.

---

## Internal Settings

The following settings exist in the database but are **not shown** in the admin settings UI. They are managed automatically by the application and should not be modified manually. The API blocks both reads and writes for these keys.

| Key | Category | Description |
|---|---|---|
| `migration_completed` | system | Sentinel flag indicating that the one-time data migration from `pweb_settings` to structured tables has completed. Set automatically by `SettingsMigrationStartupService`. |

## Sensitive Settings

These settings are stored in the database but hidden from the admin settings UI groups. Unlike internal settings, they **are** accessible to admin users via the API. Prefer configuring them via [appsettings](reference.md) environment variables instead.

| Key | Category | Description |
|---|---|---|
| `api_address` | api | Poracle API address. Prefer `Poracle:ApiAddress` in [appsettings](reference.md). |
| `api_secret` | api | Poracle API shared secret. Prefer `Poracle:ApiSecret` in [appsettings](reference.md). |
| `telegram_bot_token` | telegram | Telegram bot token. Prefer `Telegram:BotToken` in [appsettings](reference.md). |
| `scan_db` | database | Scanner database connection string. Prefer `ConnectionStrings:ScannerDb` in [appsettings](reference.md). |
