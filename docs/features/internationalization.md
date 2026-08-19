# Internationalization (i18n)

PoracleWeb.NET supports 11 UI languages, matching the language support from the original PoracleWeb.NET PHP. Users can switch the interface language at any time without reloading the page.

## Supported Languages

| Flag | Language | Code | Completeness |
|---|---|---|---|
| :flag_gb: | English | `en` | Full (baseline) |
| :flag_fr: | Français | `fr` | Full |
| :flag_de: | Deutsch | `de` | Full |
| :flag_es: | Español | `es` | Full |
| :flag_nl: | Nederlands | `nl` | Full |
| :flag_it: | Italiano | `it` | Full |
| :flag_pt: | Português | `pt` | Full |
| :flag_br: | Português (BR) | `pt-BR` | Full |
| :flag_pl: | Polski | `pl` | Full |
| :flag_dk: | Dansk | `da` | Full |
| :flag_se: | Svenska | `sv` | Full |

## How It Works

### Frontend (Angular)

The UI translation system uses [ngx-translate](https://github.com/ngx-translate/core) for runtime language switching:

- **Translation files** are stored in `ClientApp/src/assets/i18n/{code}.json` as flat namespaced JSON
- **Language detection** — on first visit, the browser's preferred language is auto-detected
- **Persistence** — the selected language is stored in `localStorage('poracle-ui-language')`
- **Instant switching** — changing language updates all visible text immediately, no page reload needed

### Language selectors

There are two, and they sit next to each other in the **user menu** (top-right toolbar):

- **Display language** changes this site's text and nothing else. Its submenu is hidden when an admin has restricted the selector to a single language.
- **Alert language** is what Poracle writes your DMs in: alert text, Pokemon names, move names. The authoritative copy lives on your Poracle account (`humans.language`), with a browser cache used only for the first render, so it follows you between devices and reconciles if the bot changes it.

Each submenu opens with its own hint line ("Changes this site's text only." / "Used for alert text and Pokemon names.") and lists the languages as flag and native name, with a check mark against the active one. Both draw from the same list of 11.

The two settings are independent. A German UI with English Pokemon names is a normal thing to want, and the menu now shows that they are separate rather than leaving it to a footnote.

!!! note "This moved"
    The alert language used to live on the Areas page. It is in the user menu as of the Areas and Places merge, alongside the display language it kept being confused with.

### Admin Configuration

Admins can restrict which languages appear in the selector by setting the `allowed_languages` site setting:

| Setting | Value | Effect |
|---|---|---|
| `allowed_languages` | *(empty)* | All 11 languages available |
| `allowed_languages` | `en,de,fr` | Only English, German, and French shown |

English is always available regardless of the `allowed_languages` setting.

Set this in **Admin → Settings** under the **Features** category.

## Translation File Structure

Each language file uses namespaced keys organized by feature area:

```json
{
  "NAV": {
    "DASHBOARD": "Dashboard",
    "POKEMON": "Pokemon",
    "RAIDS": "Raids"
  },
  "MENU": {
    "PAUSE_ALERTS": "Pause Alerts",
    "LOGOUT": "Logout"
  },
  "DASHBOARD": {
    "TITLE": "Dashboard",
    "WELCOME": "Welcome back, {{username}}"
  }
}
```

### Key Namespaces

| Namespace | Content |
|---|---|
| `NAV` | Navigation sidebar labels |
| `TOOLBAR` | Toolbar buttons and tooltips |
| `BANNER` | Status banners (impersonation, paused, disabled) |
| `MENU` | User menu items |
| `SHORTCUTS` | Keyboard shortcut overlay |
| `TOAST` / `HTTP_ERROR` | Toast notifications and HTTP error messages |
| `DASHBOARD` | Dashboard page |
| `POKEMON` | Pokemon alarm management |
| `RAIDS` | Raid & egg alarm management |
| `QUESTS` | Quest alarm management |
| `INVASIONS` | Invasion alarm management |
| `LURES` | Lure alarm management |
| `NESTS` | Nest alarm management |
| `GYMS` | Gym alarm management |
| `FORT_CHANGES` | Fort change alarm management |
| `MAX_BATTLES` | Max battle alarm management |
| `AREAS` | Areas & Places page |
| `PROFILES` | Profile management |
| `GEOFENCES` | Custom geofences |
| `CLEANING` | Clean mode settings |
| `QUICK_PICKS` | Quick pick alarm presets |
| `HELP` | Help page: section titles, search, and the guide body HTML |
| `AUTH` | Login page |
| `ADMIN` | Admin pages |
| `ALARM` | Shared alarm dialog fields |
| `DIALOG` | Shared dialog components |
| `TEST_ALERT` | Test alert feedback |
| `WHERE` | Per-alarm delivery scope and saved places |
| `ALERT_DEFAULTS` | Alert Defaults dialog |
| `ALARM_INFO` | Shared alarm summary component |
| `ACTIVE_HOURS_CHIP` | Profile schedule pills |
| `LOCATION_WARNING` | Missing-coordinates warning banner |
| `ONBOARDING` | First-run wizard |
| `DELIVERY_PREVIEW` | Delivery preview map |
| `AREA_MAP` | Shared area map component |
| `REGION_SELECTOR` | Region picker for geofence submission |
| `GEOFENCE_DETAIL` | Geofence detail view |
| `GEOJSON_IMPORT` | GeoJSON import dialog |
| `GYM_PICKER` | Gym autocomplete |
| `POKEMON_SELECTOR` | Species picker |
| `TEMPLATE` / `TEMPLATE_SELECTOR` | Notification template picking and preview |
| `ADMIN_SETTINGS` | Admin settings page |
| `PAGINATOR` | Material paginator labels |
| `ERROR` | Error page and interceptor messages |
| `COMMON` | Common labels (Save, Cancel, Delete, etc.) |

### Interpolation

Dynamic values use double-brace syntax: `{{variable}}`. These must be preserved exactly in translations:

```json
{
  "WELCOME": "Welcome back, {{username}}",
  "AREAS_COUNT": "{{count}} area(s) active"
}
```

### HTML in Translations

Some values contain HTML tags (mainly `<strong>`) for emphasis. These must be preserved in translations and rendered with `[innerHTML]` binding:

```json
{
  "PAUSED_ALERTS": "Your alerts are <strong>paused</strong>. You will not receive notifications."
}
```

## Contributing Translations

To improve or add translations:

1. Edit the relevant `src/assets/i18n/{code}.json` file
2. Ensure all keys from `en.json` are present (missing keys fall back to English)
3. Preserve `{{placeholders}}` and HTML tags exactly
4. Keep technical terms untranslated: Pokemon, Discord, Poracle, DM, IV, CP, PVP, ATK, DEF, STA
5. Keep game proper nouns: Mystic, Valor, Instinct, Giovanni, Team Rocket, Dynamax, Gigantamax, PokéStop
6. Use informal forms (du/tu/tú/je) appropriate for a gaming community

### What Is NOT Translated

- **Pokemon names, move names, form names** — these come from Poracle's master data, controlled by the alert language setting in the user menu
- **Admin-configured values** — site title, logo, custom navigation links
- **User-generated content** — profile names, geofence names, area names

The help guide is translated, body and all: the `HELP.CONTENT_*` values carry the HTML for each section and every locale has its own. The gap runs the other way now, and it is small: 30 of the 36 `HELP.SECTION_*` headings are still English in Dutch, Polish and Portuguese.

## Architecture

```
ClientApp/
  src/
    assets/i18n/           # Translation JSON files
      en.json              # English (baseline, ~1,700 keys)
      de.json              # German
      fr.json              # French
      ...
    app/
      core/services/
        i18n.service.ts    # Language management service
      app.config.ts        # ngx-translate provider setup
```

The `I18nService`:

- Wraps `@ngx-translate/core`'s `TranslateService`
- Manages available languages (filtered by admin `allowed_languages` setting)
- Handles browser language detection on first visit
- Provides `instant()` for synchronous translation in TypeScript code
- Sets `document.documentElement.lang` for accessibility

Translation files are loaded lazily via HTTP — only the active language file is fetched. Switching languages fetches the new file and caches it for the session.
