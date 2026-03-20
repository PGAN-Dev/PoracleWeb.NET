import { Injectable, inject, computed } from '@angular/core';

import { SettingsService } from './settings.service';

const DEFAULT_UICONS = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';

@Injectable({ providedIn: 'root' })
export class IconService {
  private readonly settings = inject(SettingsService);

  private readonly gymBase = computed(() => (this.settings.siteSettings()['uicons_gym'] || `${DEFAULT_UICONS}/gym`).replace(/\/$/, ''));

  private readonly itemBase = computed(() => (this.settings.siteSettings()['uicons_item'] || `${DEFAULT_UICONS}/item`).replace(/\/$/, ''));

  private readonly pkmnBase = computed(() =>
    (this.settings.siteSettings()['uicons_pkmn'] || `${DEFAULT_UICONS}/pokemon`).replace(/\/$/, ''),
  );

  private readonly raidBase = computed(() => (this.settings.siteSettings()['uicons_raid'] || `${DEFAULT_UICONS}/raid`).replace(/\/$/, ''));

  private readonly rewardBase = computed(() =>
    (this.settings.siteSettings()['uicons_reward'] || `${DEFAULT_UICONS}/reward`).replace(/\/$/, ''),
  );

  /** Get the base URLs for preview purposes */
  getBases() {
    return {
      raid: this.raidBase(),
      gym: this.gymBase(),
      pokemon: this.pkmnBase(),
      reward: this.rewardBase(),
    };
  }

  getGymUrl(team: number): string {
    return `${this.gymBase()}/${team}.png`;
  }

  getItemUrl(id: number): string {
    return `${this.rewardBase()}/item/${id}.png`;
  }

  getPokemonFallbackUrl(id: number): string {
    if (id === 0) return '';
    return `${this.pkmnBase()}/${id}.png`;
  }

  getPokemonUrl(id: number, form?: number): string {
    if (id === 0) return '';
    const formSuffix = form && form > 0 ? `_f${form}` : '';
    return `${this.pkmnBase()}/${id}${formSuffix}.png`;
  }

  getRaidEggUrl(level: number): string {
    return `${this.raidBase()}/egg/${level}.png`;
  }

  getRewardUrl(type: string, id: number): string {
    return `${this.rewardBase()}/${type}/${id}.png`;
  }
}
