# Internationalization (i18n)

PoracleWeb supports 11 UI languages, matching the language support from the original PoracleWeb PHP. Users can switch the interface language at any time without reloading the page.

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

### Language Selector

Users access the language selector from the **user menu** (top-right toolbar) → **Language** submenu. Each language shows its flag emoji and native name. The currently active language is indicated with a check mark.

!!! note "Bot Language vs UI Language"
    The **UI language** (this feature) controls the web interface language. The **bot language** (set in Areas & Location → Language) controls what language Poracle sends DMs in (Pokemon names, move names, etc.). These are separate settings — a user can have a German UI with English Pokemon names, for example.

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
| `AREAS` | Areas & location page |
| `PROFILES` | Profile management |
| `GEOFENCES` | Custom geofences |
| `CLEANING` | Clean mode settings |
| `QUICK_PICKS` | Quick pick alarm presets |
| `HELP` | Help page chrome (section titles, search) |
| `AUTH` | Login page |
| `ADMIN` | Admin pages |
| `ALARM` | Shared alarm dialog fields |
| `DIALOG` | Shared dialog components |
| `TEST_ALERT` | Test alert feedback |
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

- **Pokemon names, move names, form names** — these come from Poracle's master data, controlled by the bot language setting
- **Admin-configured values** — site title, logo, custom navigation links
- **User-generated content** — profile names, geofence names, area names
- **Help guide body content** — section titles are translated, but detailed help content remains in English (contributions welcome)

## Architecture

```
ClientApp/
  src/
    assets/i18n/           # Translation JSON files
      en.json              # English (baseline, ~500 keys)
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
