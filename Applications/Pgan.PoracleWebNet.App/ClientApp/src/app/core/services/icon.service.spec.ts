import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ICON_SOURCE_FOLDERS, ICON_SOURCE_KEYS, IconService } from './icon.service';
import { SettingsService } from './settings.service';

describe('IconService', () => {
  let service: IconService;
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
    service = TestBed.inject(IconService);
    siteSettings.set({});
  });

  describe('with default settings', () => {
    // Repointed with #877: whitewillem/PogoAssets no longer exists -- the repository 404s, not just the
    // path -- so this constant was pinning the service to a dead host and the suite was defending it.
    const DEFAULT_BASE = 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS';

    it('should return default pokemon URL', () => {
      expect(service.getPokemonUrl(25)).toBe(`${DEFAULT_BASE}/pokemon/25.png`);
    });

    it('should return empty string for pokemon ID 0', () => {
      expect(service.getPokemonUrl(0)).toBe('');
    });

    it('should include form suffix when form > 0', () => {
      expect(service.getPokemonUrl(25, 61)).toBe(`${DEFAULT_BASE}/pokemon/25_f61.png`);
    });

    it('should not include form suffix for form 0', () => {
      expect(service.getPokemonUrl(25, 0)).toBe(`${DEFAULT_BASE}/pokemon/25.png`);
    });

    it('should return fallback pokemon URL without form', () => {
      expect(service.getPokemonFallbackUrl(25)).toBe(`${DEFAULT_BASE}/pokemon/25.png`);
    });

    it('should return empty string for fallback with ID 0', () => {
      expect(service.getPokemonFallbackUrl(0)).toBe('');
    });

    it('should return gym URL', () => {
      expect(service.getGymUrl(1)).toBe(`${DEFAULT_BASE}/gym/1.png`);
    });

    it('should return raid egg URL', () => {
      expect(service.getRaidEggUrl(5)).toBe(`${DEFAULT_BASE}/raid/egg/5.png`);
    });

    it('should return reward URL', () => {
      expect(service.getRewardUrl('item', 42)).toBe(`${DEFAULT_BASE}/reward/item/42.png`);
    });

    it('should return bases for preview', () => {
      const bases = service.getBases();
      expect(bases.pokemon).toContain('pokemon');
      expect(bases.raid).toContain('raid');
      expect(bases.gym).toContain('gym');
      expect(bases.reward).toContain('reward');
    });
  });

  describe('with custom settings', () => {
    it('should use custom pokemon base URL', () => {
      siteSettings.set({ uicons_pkmn: 'https://custom.cdn/pokemon' });

      expect(service.getPokemonUrl(25)).toBe('https://custom.cdn/pokemon/25.png');
    });

    it('should strip trailing slash from custom URL', () => {
      siteSettings.set({ uicons_pkmn: 'https://custom.cdn/pokemon/' });

      expect(service.getPokemonUrl(25)).toBe('https://custom.cdn/pokemon/25.png');
    });

    it('should use custom gym base URL', () => {
      siteSettings.set({ uicons_gym: 'https://custom.cdn/gym' });

      expect(service.getGymUrl(2)).toBe('https://custom.cdn/gym/2.png');
    });

    it('should use custom raid base URL', () => {
      siteSettings.set({ uicons_raid: 'https://custom.cdn/raid' });

      expect(service.getRaidEggUrl(3)).toBe('https://custom.cdn/raid/egg/3.png');
    });

    it('should use custom reward base URL', () => {
      siteSettings.set({ uicons_reward: 'https://custom.cdn/reward' });

      expect(service.getRewardUrl('stardust', 1)).toBe('https://custom.cdn/reward/stardust/1.png');
    });

    it('should use custom invasion base URL', () => {
      siteSettings.set({ uicons_invasion: 'https://custom.cdn/invasion' });

      expect(service.getInvasionUrl(41)).toBe('https://custom.cdn/invasion/41.png');
    });
  });

  /**
   * The constant tables of grunt and event artwork name a picture the way it sits inside a pack and
   * cannot inject anything to ask where pictures live. Before #877 they carried whole URLs built from
   * their own copy of a base, which is how they kept requesting a repository that had been deleted.
   */
  describe('getPackUrl', () => {
    const DEFAULT_BASE = 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS';

    it('resolves each folder against that category, not one shared base', () => {
      siteSettings.set({
        uicons_invasion: 'https://invasions.cdn/invasion',
        uicons_pkmn: 'https://mons.cdn/pokemon',
        uicons_type: 'https://types.cdn/type',
      });

      expect(service.getPackUrl('invasion/41.png')).toBe('https://invasions.cdn/invasion/41.png');
      expect(service.getPackUrl('type/7.png')).toBe('https://types.cdn/type/7.png');
      expect(service.getPackUrl('pokemon/352.png')).toBe('https://mons.cdn/pokemon/352.png');
    });

    it('keeps the part of the path below the folder', () => {
      expect(service.getPackUrl('reward/item/501.png')).toBe(`${DEFAULT_BASE}/reward/item/501.png`);
    });

    it('tolerates a leading slash', () => {
      expect(service.getPackUrl('/gym/1.png')).toBe(`${DEFAULT_BASE}/gym/1.png`);
    });

    it('serves a folder this build has no setting for from the same host as everything else', () => {
      // Packs carry weather/, team/, station/ and more. None of them has a setting yet, and a caller
      // asking for one should get a URL rather than a broken relative path.
      expect(service.getPackUrl('weather/3.png')).toBe(`${DEFAULT_BASE}/pokemon/weather/3.png`);
    });

    it('honours every declared source key, not just the three above', () => {
      // A key added to SOURCES without getPackUrl learning its folder would quietly resolve under the
      // Pokemon base -- a plausible-looking URL for a picture that is not there.
      siteSettings.set(Object.fromEntries(ICON_SOURCE_KEYS.map(key => [key, `https://${key}.test`])));

      const wrong = ICON_SOURCE_KEYS.filter(key => service.getPackUrl(`${ICON_SOURCE_FOLDERS[key]}/1.png`) !== `https://${key}.test/1.png`);

      expect(wrong).toEqual([]);
    });
  });
});
