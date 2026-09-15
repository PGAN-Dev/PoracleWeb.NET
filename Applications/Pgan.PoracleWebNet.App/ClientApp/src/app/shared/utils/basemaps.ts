/**
 * The tile layers a PoracleWeb.NET install can draw its maps with.
 *
 * One catalogue rather than a URL typed into a settings box, because a basemap is never just a URL:
 * it is a URL, the attribution its licence requires, the zoom it stops serving at, and — for the
 * providers that publish one — a second URL for the dark theme. Splitting those across four admin
 * fields makes every one of them an independent way to get it wrong. Picking a name sets all four.
 *
 * A custom URL is still accepted (`basemap_url`), and is how anything not listed here gets used.
 */
export interface BasemapDefinition {
  /**
   * Attribution HTML, shown by Leaflet's attribution control. Fixed per provider because it is a
   * licence term, not a preference. The one admin-supplied attribution (for a custom URL) is escaped
   * before it reaches Leaflet, which sets attribution as innerHTML.
   */
  attribution: string;

  /** Template used while the dark theme is active. Providers without a dark style reuse {@link url}. */
  darkUrl?: string;

  /** Stored in `basemap_provider` and in each user's own choice. Renaming one resets both. */
  id: string;

  /**
   * Providers sharing a key family are unlocked by the same `basemap_key`, so an admin who supplies a
   * CARTO key gets every CARTO style rather than only the one they nominated. Absent means keyless.
   */
  keyFamily?: string;

  /** Provider name. Deliberately untranslated: these are brand names. */
  label: string;

  maxZoom: number;

  subdomains?: string;

  /** Tile URL template. `{key}` is substituted by {@link applyBasemapKey}; the rest is Leaflet's. */
  url: string;
}

const CARTO_ATTRIBUTION =
  '&copy; <a href="https://carto.com/attributions">CARTO</a> &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

const ESRI_ATTRIBUTION = 'Tiles &copy; <a href="https://www.esri.com/">Esri</a>';

const OSM_ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

const STADIA_ATTRIBUTION =
  '&copy; <a href="https://stadiamaps.com/">Stadia Maps</a> &copy; <a href="https://openmaptiles.org/">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

/** Id of the entry assembled from `basemap_url` / `basemap_url_dark` / `basemap_attribution`. */
export const CUSTOM_BASEMAP_ID = 'custom';

/**
 * What an install with nothing configured asks for. CARTO, because that is what every map used
 * before this was configurable and changing the look of working installs is not this feature's job.
 * Without a key it never actually renders — see {@link FALLBACK_BASEMAP_ID}.
 */
export const DEFAULT_BASEMAP_ID = 'carto-positron';

/**
 * Where a keyed provider lands when its key is missing.
 *
 * Not a cosmetic choice. CARTO answers 200 without a key and draws "API KEY REQUIRED" into the tile
 * itself, so serving the keyed URL anyway produces a map that looks broken and reports nothing. A
 * keyless provider is worse-looking than the intended basemap and better than a watermark. See #842.
 */
export const FALLBACK_BASEMAP_ID = 'osm';

export const BUILTIN_BASEMAPS: readonly BasemapDefinition[] = [
  {
    id: 'osm',
    attribution: OSM_ATTRIBUTION,
    label: 'OpenStreetMap',
    maxZoom: 19,
    url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  },
  {
    id: 'carto-positron',
    attribution: CARTO_ATTRIBUTION,
    darkUrl: 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png?key={key}',
    keyFamily: 'carto',
    label: 'CARTO Positron',
    maxZoom: 20,
    subdomains: 'abcd',
    url: 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key={key}',
  },
  {
    id: 'carto-voyager',
    attribution: CARTO_ATTRIBUTION,
    keyFamily: 'carto',
    label: 'CARTO Voyager',
    maxZoom: 20,
    subdomains: 'abcd',
    url: 'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png?key={key}',
  },
  {
    id: 'stadia-smooth',
    attribution: STADIA_ATTRIBUTION,
    darkUrl: 'https://tiles.stadiamaps.com/tiles/alidade_smooth_dark/{z}/{x}/{y}{r}.png?api_key={key}',
    keyFamily: 'stadia',
    label: 'Stadia Alidade Smooth',
    maxZoom: 20,
    url: 'https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png?api_key={key}',
  },
  {
    id: 'esri-canvas',
    attribution: ESRI_ATTRIBUTION,
    darkUrl: 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}',
    label: 'Esri Gray Canvas',
    // Esri stops serving the Canvas basemaps above 16. Leaflet clamps the map to the layer's maxZoom,
    // so the effect is that this one cannot be zoomed as far in, not that it goes blank.
    maxZoom: 16,
    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Light_Gray_Base/MapServer/tile/{z}/{y}/{x}',
  },
  {
    id: 'esri-imagery',
    attribution: ESRI_ATTRIBUTION,
    label: 'Esri World Imagery',
    maxZoom: 19,
    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
  },
];

/**
 * Substitutes the provider key into a tile template.
 *
 * Done here and not by Leaflet: its own template helper throws on a placeholder it has no value for,
 * and the encoding is ours to get right. A key is a query-string value, so a `+` or `/` in one has to
 * survive as `%2B` and `%2F` or the provider reads a different key and — for CARTO — watermarks.
 */
export function applyBasemapKey(template: string, key: string): string {
  return template.replaceAll('{key}', encodeURIComponent(key));
}

/** The catalogue entry with this id, or null. Unknown ids come from a typo in `basemap_provider`. */
export function findBasemap(id: string | null | undefined): BasemapDefinition | null {
  if (!id) return null;
  return BUILTIN_BASEMAPS.find(b => b.id === id) ?? null;
}
