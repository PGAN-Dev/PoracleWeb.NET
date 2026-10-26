---
paths:
  - "**/*Location*"
  - "**/*location*"
  - "**/*basemap*"
  - "**/*Gym*"
  - "**/*gym*"
  - "**/Scanner*"
  - "**/*scanner*"
  - "**/PlaceUsage*"
  - "**/*places*"
  - "**/place-label*"
  - "**/area-map/**"
  - "**/scope-map/**"
  - "**/area-overview-map/**"
---

## PoracleNG Geocode Proxy

Address search and reverse lookup (`LocationController.Geocode`/`ReverseGeocode`) go through
`IPoracleApiProxy.GetGeocodeForwardAsync`/`GetGeocodeReverseAsync`, which call PoracleNG's own
`/api/geocode/forward` and `/api/geocode/reverse` -- **not** a geocoder provider URL. PoracleNG already
normalises Nominatim, Photon and Google into one shape (`displayName`, `city`, `state`, `country`,
`streetName`, `streetNumber`, `zipcode`, numeric `latitude`/`longitude` on forward; the richer `Address`
struct -- `formattedAddress`, `neighbourhood`, `county`, `suburb`, `town`, `village`, `addr`, `flag` --
on reverse), so this app never parses a provider's payload and gains every provider PoracleNG supports
for free. See #845: an earlier draft of that issue proposed a `geocoder_provider` site setting and
per-provider parsing here, which was rejected as duplicating work Poracle already does and creating a
second place to configure the geocoder that can disagree with the first.

**There is no fallback for a PoracleNG without either route.** Both shipped in 5.4.0 (jfberry/PoracleNG#224), and
the code that called the configured provider directly and spoke to it as Nominatim is deleted. A plaintext
`404 page not found` (`PoracleProblemDetails.IsProblemJson` says so, the same check the v2 proxies use to
tell a missing route from a real answer) throws `PoracleUnsupportedException("Address search"...,
"PoracleNG 5.4.0")`, which the filter reports as 409. A *problem+json* 404 from `reverse` still means
"nothing at this coordinate", including every call when `[geocoding] forward_only` is set, which disables
reverse deliberately, and must never read as "too old". `LocationService` swallows the 409, so on a
too-old server the address search returns nothing.

The SPA's `GeocodingResult`/`ReverseGeocodingResult` (`core/models/index.ts`) are flat and read PoracleNG's
field names directly (`displayName`, not `display_name`; numeric `latitude`/`longitude`, not string
`lat`/`lon`) -- there is no `address.*` nesting left over from Nominatim's shape. `location-dialog`'s
`getAddressPrimary`/`getAddressSecondary`/`getPlaceIcon` build their own grouping from the flat fields;
`getPlaceIcon` infers an icon from which fields are present, since PoracleNG's forward result carries
no OSM class/type the way Nominatim's did.

`disable_nominatim` still gates both actions and keeps its name regardless of which provider or surface
answers behind it (see "Feature Gating" above).

## Pin And Place Usage, Read Before A Destructive Action

`GET /api/location/pin-usage` answers `{ alarms }`, the active profile's rules that measure from the pin (a radius
above zero and no saved place), and the Areas page names that count before Clear pin. `GET /api/location/place-usage?label=`
answers `{ alarms, rules }`, every rule aimed at a saved place, Pokéstop events included, with PoracleNG's
description of each (`GetAllTrackingWithDescriptionsAsync`, `?include_descriptions=true`); the places section lists
them instead of offering Delete, because PoracleNG refuses to delete a place that rules still point at (the delete's
own 409 covers a rule added in between). Both sit under the controller's `disable_location` gate, both are in the
error interceptor's silent list (a failed pre-check falls back to the plain confirm), and both share the per-user
`tracking-usage` rate limit. Active profile only, as every tracking read is.

## Gym Picker
- `GymPickerComponent` is a shared autocomplete component (`shared/components/gym-picker/`) for selecting a gym by name. Used in gym, raid, and egg add/edit dialogs to populate `gym_id`.
- Displays gym photo thumbnails (from `ScannerGymEntity.Url`), name, and resolved area name in the dropdown options.
- Backed by `ScannerService` (frontend, `core/services/scanner.service.ts`) which calls two scanner API endpoints:
  - `GET /api/scanner/gyms?search=term&limit=20` -- Searches gyms by name prefix in the scanner DB. Returns `GymSearchResult[]` with `id`, `name`, `url`, `lat`, `lon`, `teamId`, `area`.
  - `GET /api/scanner/gyms/{id}` -- Looks up a single gym by ID. Used to resolve the display name for an existing `gym_id` value when opening an edit dialog.
- Area names are resolved server-side via point-in-polygon against cached Koji admin geofences. The `IScannerService.PointInPolygon()` static method uses ray-casting for hit testing.
- `ScannerGymEntity` maps the `url` column for gym photo thumbnails from the scanner DB.
- The scanner DB is optional -- if not configured, the gym picker is hidden and `gym_id` can still be entered manually.

## Basemaps: A Tile Provider Refusing You Still Answers 200

`BasemapService` builds every tile layer on the site; the five components call `attach(map, …)` and
nothing else. The catalogue is `shared/utils/basemaps.ts`, the settings are `basemap_*`, and the
user-facing reference is `docs/configuration/site-settings.md#maps`.

**The failure mode that keeps recurring: the refusal is drawn into the tile.** CARTO answers 200
without a key and returns a working image with `API KEY REQUIRED` across it. OpenStreetMap answers
200 without a `Referer` and returns `403 Access blocked` as a picture. Nothing logs, no health check
notices, and the browser's network tab shows success. #842 was this, and the fix for it walked into
it a second time with OSM. **Never conclude tiles work because the status code is 200 — look at the
bytes** (a refusal tile is ~7 KB, a real one much larger) **or at the image.**

A *missing* key is detectable and falls back to `FALLBACK_BASEMAP_ID`. A *wrong* key is not: the
response is byte-identical to the no-key one. Do not add code that claims otherwise.

**`Referrer-Policy: same-origin` (#383) is why OSM needs `sendReferrer`.** The header strips the
identification OSM's usage policy requires. The fix is Leaflet's per-layer `referrerPolicy`, which
overrides the document policy for those images alone — *not* relaxing the header, which exists so a
remote image host cannot learn where a private instance lives. `sendReferrer` is per provider because
OSM is the only entry needing it; Esri and CARTO return the same bytes either way, which was checked
by fetching them rather than assumed.

**Three constants decide what "default" means and they must agree** — `DEFAULT_BASEMAP_ID` (nothing
configured), `KEYED_DEFAULT_BASEMAP_ID` (a key and nothing else, which is all the pre-#863 settings
could say), `FALLBACK_BASEMAP_ID` (the chosen provider cannot be drawn). #871 happened because
#868 changed the fallback and left the default stale: the map drew OSM while the admin page reported
a missing CARTO key on an install that had never mentioned CARTO. Change one, check the other two.

A fourth, `DARK_DEFAULT_BASEMAP_ID` (Esri dark gray, keyless), is what the dark theme draws for a viewer
on the site default whose provider has no dark style. `siteDefault` stays theme-independent because the
admin page reports it; `themedDefault` is what is drawn and what the picker's *Site default* entry names.
Imagery (`keepInDark`) and a custom URL are never swapped. **Esri's Canvas basemaps stop at zoom 16**, so the
definition sets `maxNativeZoom: 16` with `maxZoom: 19`: Leaflet scales the zoom-16 tiles up past 16 instead of clamping
the map, and a dark-theme viewer zooms as far as OSM allows (a map attached with `{ maxZoom: 18 }` still takes the smaller).
Setting `maxZoom: 16` instead clamped the whole map, which made placing a pin by a single stop impossible in dark mode.

**A viewer's own choice outranks the admin setting**, stored per browser. That is the feature, but it
needs the escape: the picker's first entry is *Site default*, and the active mark follows the
viewer's choice rather than what is drawn, so an override reads as one. Without it an admin who
clicked the layers button once concludes the setting does nothing — which is exactly how it was
reported.

**`ng serve` sends none of `SecurityHeaders`.** CSP and `Referrer-Policy` come from the .NET
middleware, which only runs when the API serves the SPA. Anything depending on them cannot be
reproduced *or ruled out* against the dev server; the OSM breakage looked perfect in every local
browser check and appeared the moment it deployed. Build the image and hit the API's own port.

Four tile URLs are pinned in `basemap.service.spec.ts` against ReactMap's `config/default.json`, so
the same basemap looks the same on both sites and a catalogue edit cannot drift silently.

## Scanner DB is Optional
The `ScannerDb` connection string is optional. If not configured, `IScannerService` is not registered and scanner endpoints return appropriate responses. The gym search endpoints (`GET /api/scanner/gyms?search=` and `GET /api/scanner/gyms/{id}`) gracefully return empty results when the scanner DB is unavailable, and the `GymPickerComponent` hides itself in the UI.
