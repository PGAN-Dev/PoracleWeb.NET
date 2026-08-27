import { Injectable, computed, inject } from '@angular/core';
import * as L from 'leaflet';

import { SettingsService } from './settings.service';

/**
 * CARTO's basemaps now require a key. An unkeyed request still answers 200 and still returns usable
 * tiles, so the only signal is an "API KEY REQUIRED" watermark drawn into the image itself: nothing
 * logs, no health check notices, and the maps look broken only to whoever is looking at them. See #842.
 *
 * `{key}` is substituted here rather than handed to Leaflet, because Leaflet's template helper throws
 * on a placeholder it has no value for, and because the encoding is ours to get right.
 */
const DEFAULT_URL = 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key={key}';

const DEFAULT_ATTRIBUTION =
  '&copy; <a href="https://carto.com/">CARTO</a> &copy; <a href="https://www.openstreetmap.org/copyright">OSM</a>';

@Injectable({ providedIn: 'root' })
export class BasemapService {
  private readonly settings = inject(SettingsService);

  private readonly attribution = computed(() => this.settings.siteSettings()['basemap_attribution'] || DEFAULT_ATTRIBUTION);

  private readonly key = computed(() => (this.settings.siteSettings()['basemap_key'] || '').trim());

  private readonly urlTemplate = computed(() => this.settings.siteSettings()['basemap_url'] || DEFAULT_URL);

  /**
   * True when the configured tile URL asks for a key and no key is set. A caller should say so out
   * loud: the tiles will render, watermarked, and look like a styling bug rather than a missing
   * setting. An operator who has pointed `basemap_url` at a keyless provider gets false, not a
   * warning about a key their URL never wanted.
   */
  readonly missingKey = computed(() => this.urlTemplate().includes('{key}') && this.key() === '');

  /**
   * The single tile layer every map on the site uses.
   *
   * `maxZoom` is overridable because the five call sites this replaced had already drifted apart, and
   * one of them caps at 18. Three carried no attribution at all, which centralising fixes on its own.
   */
  createLayer(options?: { maxZoom?: number }): L.TileLayer {
    return L.tileLayer(this.tileUrl(), {
      attribution: this.attribution(),
      maxZoom: options?.maxZoom ?? 19,
      subdomains: 'abcd',
    });
  }

  /** Exposed for tests and for anything that needs the URL without a Leaflet map to attach it to. */
  tileUrl(): string {
    return this.urlTemplate().replace('{key}', encodeURIComponent(this.key()));
  }
}
