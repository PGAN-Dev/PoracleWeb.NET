import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BasemapService } from './basemap.service';
import { SettingsService } from './settings.service';

describe('BasemapService', () => {
  let service: BasemapService;
  const siteSettings = signal<Record<string, string>>({});

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SettingsService,
          useValue: { siteSettings },
        },
      ],
    });
    service = TestBed.inject(BasemapService);
    siteSettings.set({});
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

  describe('missingKey', () => {
    it('is true when the template wants a key and none is configured', () => {
      expect(service.missingKey()).toBe(true);
    });

    it('is false once a key is set', () => {
      siteSettings.set({ basemap_key: 'abc123' });
      expect(service.missingKey()).toBe(false);
    });

    it('is false for a keyless provider, which never wanted a key', () => {
      siteSettings.set({ basemap_url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png' });
      expect(service.missingKey()).toBe(false);
    });

    it('is true for a whitespace-only key, which is the same as none', () => {
      siteSettings.set({ basemap_key: '   ' });
      expect(service.missingKey()).toBe(true);
    });
  });

  describe('overrides', () => {
    it('uses a configured tile URL in place of the CARTO default', () => {
      siteSettings.set({ basemap_key: 'k', basemap_url: 'https://tiles.example/{z}/{x}/{y}.png?token={key}' });
      expect(service.tileUrl()).toBe('https://tiles.example/{z}/{x}/{y}.png?token=k');
    });

    it('applies the configured attribution to the layer', () => {
      siteSettings.set({ basemap_attribution: '&copy; Example' });
      expect(service.createLayer().options.attribution).toBe('&copy; Example');
    });

    it('defaults to CARTO and OSM attribution, which three call sites were omitting entirely', () => {
      expect(service.createLayer().options.attribution).toContain('carto.com');
      expect(service.createLayer().options.attribution).toContain('openstreetmap.org');
    });

    it('honours a maxZoom override so the overview map keeps its cap of 18', () => {
      expect(service.createLayer({ maxZoom: 18 }).options.maxZoom).toBe(18);
      expect(service.createLayer().options.maxZoom).toBe(19);
    });
  });
});
