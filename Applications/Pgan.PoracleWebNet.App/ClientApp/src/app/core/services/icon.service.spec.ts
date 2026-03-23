import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { IconService } from './icon.service';
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
    const DEFAULT_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';

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
  });
});
