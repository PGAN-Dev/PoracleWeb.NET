import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';

import { BasemapService } from './basemap.service';
import { SettingsService } from './settings.service';
import { BUILTIN_BASEMAPS } from '../../shared/utils/basemaps';

describe('BasemapService', () => {
  let service: BasemapService;
  const siteSettings = signal<Record<string, string>>({});

  /** The MutationObserver behind the theme signal fires on a microtask. */
  const settle = () => new Promise(resolve => setTimeout(resolve, 0));

  const setTheme = async (dark: boolean) => {
    document.body.classList.toggle('dark-theme', dark);
    await settle();
  };

  beforeEach(() => {
    localStorage.clear();
    document.body.className = '';

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: { siteSettings },
        },
      ],
    });
    service = TestBed.inject(BasemapService);
    siteSettings.set({});
  });

  afterEach(() => {
    document.body.className = '';
    localStorage.clear();
  });

  describe('key substitution', () => {
    it('substitutes the configured key into the default CARTO template', () => {
      siteSettings.set({ basemap_key: 'abc123' });
      expect(service.tileUrl()).toBe('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key=abc123');
    });

    it('URL-encodes a key containing reserved characters', () => {
      siteSettings.set({ basemap_key: 'a+b/c=d&e' });
      expect(service.tileUrl()).toContain('key=a%2Bb%2Fc%3Dd%26e');
    });

    it('trims surrounding whitespace, which is what a paste into an admin field leaves behind', () => {
      siteSettings.set({ basemap_key: '  abc123\n' });
      expect(service.tileUrl()).toContain('key=abc123');
    });

    it('leaves no {key} placeholder behind when no key is set', () => {
      // Leaflet's template helper throws on a placeholder it has no value for, so an unsubstituted
      // {key} would break the map outright rather than merely watermark it.
      expect(service.tileUrl()).not.toContain('{key}');
    });

    it('leaves Leaflet its own placeholders', () => {
      siteSettings.set({ basemap_key: 'abc123' });
      const url = service.tileUrl();
      for (const placeholder of ['{s}', '{z}', '{x}', '{y}', '{r}']) {
        expect(url).toContain(placeholder);
      }
    });
  });

  describe('fallbackReason', () => {
    it("names a missing key when the configured provider wants one and there isn't one", () => {
      expect(service.fallbackReason()).toBe('missing-key');
    });

    it('is null once a key is set', () => {
      siteSettings.set({ basemap_key: 'abc123' });
      expect(service.fallbackReason()).toBeNull();
    });

    it('is null for a keyless built-in, which never wanted a key', () => {
      siteSettings.set({ basemap_provider: 'osm' });
      expect(service.fallbackReason()).toBeNull();
    });

    it('is null for a keyless custom URL, which never wanted a key', () => {
      siteSettings.set({ basemap_url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png' });
      expect(service.fallbackReason()).toBeNull();
    });

    it('names a missing key for a whitespace-only one, which is the same as none', () => {
      siteSettings.set({ basemap_key: '   ' });
      expect(service.fallbackReason()).toBe('missing-key');
    });

    it('names a missing key for a custom URL that asks for one it has not been given', () => {
      siteSettings.set({ basemap_url: 'https://tiles.example/{z}/{x}/{y}.png?token={key}' });
      expect(service.fallbackReason()).toBe('missing-key');
    });

    // Found by choosing "Custom tile URL" in the admin dropdown and saving before filling the field.
    // The map came up on OpenStreetMap and nothing anywhere said why.
    it('reports a Custom provider with no tile URL at all', () => {
      siteSettings.set({ basemap_provider: 'custom' });
      expect(service.fallbackReason()).toBe('unavailable');
    });

    it('reports a Custom provider whose tile URL is not an absolute http(s) template', () => {
      siteSettings.set({ basemap_provider: 'custom', basemap_url: 'tiles.example.com/{z}/{x}/{y}.png' });
      expect(service.fallbackReason()).toBe('unavailable');
    });

    it('reports a provider id this build does not know, which a rollback can leave behind', () => {
      siteSettings.set({ basemap_provider: 'carto-something-newer' });
      expect(service.fallbackReason()).toBe('unavailable');
    });

    it('is null when the configured provider is the one being drawn', () => {
      siteSettings.set({ basemap_provider: 'esri-imagery' });
      expect(service.active().id).toBe('esri-imagery');
      expect(service.fallbackReason()).toBeNull();
    });
  });

  describe('missing-key fallback', () => {
    // CARTO answers 200 without a key and draws "API KEY REQUIRED" into the tile, so serving the
    // keyed URL anyway is the one outcome that reports nothing at all. See #842.
    it('draws a keyless basemap rather than a watermarked one', () => {
      expect(service.active().id).toBe('osm');
      expect(service.tileUrl()).toBe('https://tile.openstreetmap.org/{z}/{x}/{y}.png');
    });

    it('uses the configured provider as soon as it has a key', () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      expect(service.active().id).toBe('carto-voyager');
    });

    it('does not offer a keyed provider there is no key for', () => {
      expect(service.available().map(b => b.id)).not.toContain('carto-positron');
    });

    it('falls back for a custom URL whose key is missing too', () => {
      siteSettings.set({ basemap_url: 'https://tiles.example/{z}/{x}/{y}.png?token={key}' });
      expect(service.active().id).toBe('osm');
    });
  });

  describe('available', () => {
    it('offers every style in the key family the admin nominated, not just the nominated one', () => {
      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      expect(service.available().map(b => b.id)).toEqual(expect.arrayContaining(['carto-positron', 'carto-voyager']));
    });

    it('withholds other families, which the one configured key cannot possibly unlock', () => {
      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      expect(service.available().map(b => b.id)).not.toContain('stadia-smooth');
    });

    it('always offers the keyless built-ins, which are what the fallback rests on', () => {
      expect(service.available().map(b => b.id)).toEqual(expect.arrayContaining(['osm', 'esri-imagery', 'esri-canvas']));
    });

    it('puts a configured custom URL first', () => {
      siteSettings.set({ basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.available()[0].id).toBe('custom');
    });

    it('calls the custom basemap what the admin named it', () => {
      siteSettings.set({ basemap_name: 'Richmond Tileserver', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.available()[0].label).toBe('Richmond Tileserver');
      expect(service.active().label).toBe('Richmond Tileserver');
    });

    it('falls back to "Custom", because an unnamed entry tells a viewer nothing', () => {
      siteSettings.set({ basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.available()[0].label).toBe('Custom');
    });

    it('caps a name at a length a menu entry can hold', () => {
      siteSettings.set({ basemap_name: 'x'.repeat(200), basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.available()[0].label.length).toBe(40);
    });

    it('ignores a custom URL that is not an absolute http(s) template', () => {
      siteSettings.set({ basemap_url: 'tiles.example/{z}/{x}/{y}.png' });
      expect(service.available().map(b => b.id)).not.toContain('custom');
    });
  });

  /**
   * ReactMap runs beside this site on the same deployment, and a viewer moving between the two should
   * not find the same basemap drawing different tiles. These are the URLs from its
   * `config/default.json` tileServers block, pinned so a catalogue edit here cannot drift from it
   * silently. Ours carry `?key=` where its do not -- CARTO watermarks the unkeyed ones, which is the
   * whole of #842, and ReactMap is serving them.
   */
  describe('ReactMap parity', () => {
    const withoutKey = (url: string) => url.replace('?key={key}', '');
    const entry = (id: string) => BUILTIN_BASEMAPS.find(b => b.id === id)!;

    it('draws OSM from the same tiles', () => {
      expect(entry('osm').url).toBe('https://tile.openstreetmap.org/{z}/{x}/{y}.png');
    });

    it('draws Satellite from the same tiles', () => {
      expect(entry('esri-imagery').url).toBe(
        'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
      );
    });

    it('draws Carto from the same tiles, labels under and all', () => {
      expect(withoutKey(entry('carto-voyager').url)).toBe(
        'https://{s}.basemaps.cartocdn.com/rastertiles/voyager_labels_under/{z}/{x}/{y}{r}.png',
      );
    });

    it('draws Dark Matter from the same tiles', () => {
      const darkMatter = 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png';
      expect(withoutKey(entry('carto-voyager').darkUrl!)).toBe(darkMatter);
      expect(withoutKey(entry('carto-positron').darkUrl!)).toBe(darkMatter);
    });
  });

  describe('a viewer choosing their own', () => {
    it('overrides the site default and survives a reload', () => {
      service.select('esri-imagery');
      expect(service.active().id).toBe('esri-imagery');
      expect(localStorage.getItem('poracle-basemap')).toBe('esri-imagery');
    });

    it('ignores a choice that is not on offer, rather than showing an empty map', () => {
      // A CARTO key configured yesterday and removed today leaves exactly this stored choice behind.
      service.select('carto-positron');
      expect(service.active().id).toBe('osm');
    });

    it('goes back to whatever the admin configures once the choice is cleared', () => {
      // The reported bug: an admin sets a provider, saves, and their own map does not change --
      // because they clicked the layers button once while looking at it, and the stored choice
      // outranked the setting with no way to get out of it.
      service.select('esri-imagery');
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      expect(service.active().id).toBe('esri-imagery');

      service.select('');

      expect(service.active().id).toBe('carto-voyager');
      expect(localStorage.getItem('poracle-basemap')).toBe('');
    });

    it('leaves a viewer who never chose on the admin default, whatever it becomes', () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      expect(service.active().id).toBe('carto-voyager');

      siteSettings.set({ basemap_provider: 'esri-canvas' });
      expect(service.active().id).toBe('esri-canvas');
    });
  });

  describe('dark theme', () => {
    it('serves the provider dark style when the theme is dark', async () => {
      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      await setTheme(true);
      expect(service.tileUrl()).toContain('/dark_all/');
    });

    it('goes back to the light style when the theme does', async () => {
      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      await setTheme(true);
      await setTheme(false);
      expect(service.tileUrl()).toContain('/light_all/');
    });

    it('reuses the light style for a provider that publishes no dark one', async () => {
      siteSettings.set({ basemap_provider: 'esri-imagery' });
      await setTheme(true);
      expect(service.tileUrl()).toContain('World_Imagery');
    });

    it('serves Dark Matter under Voyager, which is the pair ReactMap resolves to', async () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      expect(service.tileUrl()).toContain('/rastertiles/voyager_labels_under/');

      await setTheme(true);

      expect(service.tileUrl()).toContain('/dark_all/');
    });
  });

  describe('overrides', () => {
    it('uses a configured tile URL in place of the CARTO default', () => {
      siteSettings.set({ basemap_key: 'k', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png?token={key}' });
      expect(service.tileUrl()).toBe('https://tiles.example/{z}/{x}/{y}.png?token=k');
    });

    it('applies the configured attribution to the layer', () => {
      siteSettings.set({ basemap_attribution: 'Example tiles', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.createLayer().options.attribution).toBe('Example tiles');
    });

    it('escapes an admin attribution, which Leaflet would otherwise assign as innerHTML', () => {
      siteSettings.set({ basemap_attribution: '<img src=x onerror=alert(1)>', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png' });
      expect(service.createLayer().options.attribution).not.toContain('<img');
    });

    it('credits CARTO and OSM on the CARTO basemap, which three call sites were omitting entirely', () => {
      siteSettings.set({ basemap_key: 'abc123' });
      expect(service.createLayer().options.attribution).toContain('carto.com');
      expect(service.createLayer().options.attribution).toContain('openstreetmap.org');
    });

    it('honours a maxZoom override so the overview map keeps its cap of 18', () => {
      expect(service.createLayer({ maxZoom: 18 }).options.maxZoom).toBe(18);
      expect(service.createLayer().options.maxZoom).toBe(19);
    });
  });

  describe('attach', () => {
    const makeMap = () => {
      const element = document.createElement('div');
      document.body.appendChild(element);
      return L.map(element).setView([0, 0], 2);
    };

    /** The templates the map is actually drawing, which is the only thing worth asserting here. */
    const tileUrls = (map: L.Map): string[] => {
      const urls: string[] = [];
      map.eachLayer(layer => {
        if (layer instanceof L.TileLayer) urls.push((layer as unknown as { _url: string })._url);
      });
      return urls;
    };

    it('puts the active basemap on the map', () => {
      const map = makeMap();
      service.attach(map);

      expect(tileUrls(map).some(u => u.includes('tile.openstreetmap.org'))).toBe(true);
      map.remove();
    });

    it('follows a theme change without the caller doing anything', async () => {
      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      const map = makeMap();
      service.attach(map);

      await setTheme(true);
      TestBed.flushEffects();

      expect(tileUrls(map).some(u => u.includes('/dark_all/'))).toBe(true);
      map.remove();
    });

    it('follows a viewer switching provider, on every map that is open', () => {
      const first = makeMap();
      const second = makeMap();
      service.attach(first);
      service.attach(second);

      service.select('esri-imagery');
      TestBed.flushEffects();

      for (const map of [first, second]) {
        expect(tileUrls(map).some(u => u.includes('arcgisonline.com'))).toBe(true);
      }

      first.remove();
      second.remove();
    });

    /** The provider entries, without the site-default one that heads the list. */
    const providerLabels = (map: L.Map) =>
      Array.from(map.getContainer().querySelectorAll('.basemap-control__option'))
        .filter(b => (b as HTMLElement).dataset['basemap'])
        .map(b => b.textContent);

    it('renders a picker listing what is on offer, headed by the site default', () => {
      const map = makeMap();
      service.attach(map, { picker: true });

      expect(providerLabels(map)).toEqual(service.available().map(b => b.label));
      expect(map.getContainer().querySelector('.basemap-control__option')?.getAttribute('data-basemap')).toBe('');
      map.remove();
    });

    it('marks the site default while the viewer has not overridden it', () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      const map = makeMap();
      service.attach(map, { picker: true });

      const first = map.getContainer().querySelector('.basemap-control__option');
      expect(first?.getAttribute('data-basemap')).toBe('');
      expect(first?.classList.contains('is-active')).toBe(true);
      expect(first?.getAttribute('aria-pressed')).toBe('true');
      map.remove();
    });

    it("marks the viewer's own choice instead once they make one, so an override looks like one", () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      const map = makeMap();
      service.attach(map, { picker: true });

      service.select('esri-imagery');
      TestBed.flushEffects();

      const active = map.getContainer().querySelector('.basemap-control__option.is-active');
      expect(active?.textContent).toBe('Esri World Imagery');
      map.remove();
    });

    it('clears the override from the picker, which is the only way back', () => {
      siteSettings.set({ basemap_provider: 'carto-voyager', basemap_key: 'abc123' });
      service.select('esri-imagery');
      const map = makeMap();
      service.attach(map, { picker: true });

      map.getContainer().querySelector<HTMLButtonElement>('.basemap-control__option[data-basemap=""]')!.click();
      TestBed.flushEffects();

      expect(service.active().id).toBe('carto-voyager');
      expect(tileUrls(map).some(u => u.includes('cartocdn.com'))).toBe(true);
      map.remove();
    });

    it('switches basemap from the picker', () => {
      const map = makeMap();
      service.attach(map, { picker: true });

      const esri = Array.from(map.getContainer().querySelectorAll<HTMLButtonElement>('.basemap-control__option')).find(
        b => b.textContent === 'Esri World Imagery',
      );
      esri!.click();
      TestBed.flushEffects();

      expect(service.active().id).toBe('esri-imagery');
      expect(tileUrls(map).some(u => u.includes('arcgisonline.com'))).toBe(true);
      map.remove();
    });

    it('says so in the picker when the configured basemap has no key', () => {
      const map = makeMap();
      service.attach(map, { picker: true });

      expect(map.getContainer().querySelector('.basemap-control__warning')).not.toBeNull();
      map.remove();
    });

    it('moves the active mark without rebuilding the menu under the pointer', () => {
      const map = makeMap();
      service.attach(map, { picker: true });
      const firstOption = map.getContainer().querySelector('.basemap-control__option');

      service.select('esri-imagery');
      TestBed.flushEffects();

      // Same nodes, so the button the click landed on still exists and keeps keyboard focus.
      expect(map.getContainer().querySelector('.basemap-control__option')).toBe(firstOption);
      expect(map.getContainer().querySelector('.basemap-control__option.is-active')?.textContent).toBe('Esri World Imagery');
      map.remove();
    });

    it('says nothing about a key when the configured basemap never wanted one', () => {
      siteSettings.set({ basemap_provider: 'osm' });
      const map = makeMap();
      service.attach(map, { picker: true });

      expect(map.getContainer().querySelector('.basemap-control__warning')).toBeNull();
      map.remove();
    });

    it('says so in the picker when the configured basemap cannot be drawn at all', () => {
      siteSettings.set({ basemap_provider: 'custom' });
      const map = makeMap();
      service.attach(map, { picker: true });

      expect(map.getContainer().querySelector('.basemap-control__warning')).not.toBeNull();
      map.remove();
    });

    it('re-lists the options when the settings arrive after the map is already up', () => {
      // Settings load asynchronously at app init, so a map opened early is attached against an empty
      // configuration and has to pick up the admin's provider when it lands.
      const map = makeMap();
      service.attach(map, { picker: true });

      siteSettings.set({ basemap_provider: 'carto-positron', basemap_key: 'abc123' });
      TestBed.flushEffects();

      const labels = Array.from(map.getContainer().querySelectorAll('.basemap-control__option')).map(b => b.textContent);
      expect(labels).toContain('CARTO Positron');
      expect(tileUrls(map).some(u => u.includes('cartocdn.com'))).toBe(true);
      map.remove();
    });

    it('does not put a picker on a thumbnail', () => {
      const map = makeMap();
      service.attach(map);

      expect(map.getContainer().querySelector('.basemap-control')).toBeNull();
      map.remove();
    });

    it('stops tracking a map that has been removed', () => {
      // The geofence submissions grid tears thumbnails down from four places, none of them a
      // lifecycle hook, so the cleanup has to come off Leaflet's own unload rather than a disposer.
      const map = makeMap();
      service.attach(map);
      map.remove();

      expect(() => {
        service.select('esri-imagery');
        TestBed.flushEffects();
      }).not.toThrow();
    });
  });
});
