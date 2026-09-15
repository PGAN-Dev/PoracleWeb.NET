import { Injectable, inject, computed } from '@angular/core';

import { SettingsService } from './settings.service';
import { POKEMON_TYPE_IDS } from '../../shared/utils/pokemon-types';

/**
 * Where icons come from when an operator has set nothing.
 *
 * This pointed at whitewillem/PogoAssets, which no longer exists -- the repository 404s, not just the
 * path -- so every unset category resolved to a dead host and rendered nothing. See #877.
 *
 * A default that is one third party's repository is a hostage to that repository, which is what this
 * is. The list of packs an operator can pick from lives in the admin settings page, and making it
 * editable is the follow-up; this constant only decides what an instance shows before anyone chooses.
 */
const DEFAULT_UICONS = 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS';

/**
 * Every icon category, and the folder it lives in inside a UICONS pack.
 *
 * This is the contract between two places that have to agree: what this service READS, and what the
 * admin page's repository picker WRITES. They drifted -- the picker set four of these and left
 * `uicons_type` alone -- so an instance could look configured while its type icons still pointed at
 * the deleted default. `IconSourceKeysTests` fails the build if they drift again. See #877.
 */
const SOURCES = {
  uicons_raid: 'raid',
  uicons_gym: 'gym',
  uicons_pkmn: 'pokemon',
  uicons_reward: 'reward',
  uicons_type: 'type',
} as const;

export type IconSourceKey = keyof typeof SOURCES;

/** The settings an operator has to point somewhere for every icon on the site to resolve. */
export const ICON_SOURCE_KEYS = Object.keys(SOURCES) as IconSourceKey[];

@Injectable({ providedIn: 'root' })
export class IconService {
  private readonly settings = inject(SettingsService);

  /**
   * One category's base URL: the operator's setting, or this pack's folder under the default.
   *
   * `uicons_item` is deliberately absent. It was read here and never used -- items live at
   * `reward/item/` in every UICONS pack, so `getItemUrl` builds from the reward base -- which left a
   * setting an operator could fill in to no effect.
   */
  private readonly base = computed(() => {
    const stored = this.settings.siteSettings();

    return (key: IconSourceKey) => (stored[key] || `${DEFAULT_UICONS}/${SOURCES[key]}`).replace(/\/$/, '');
  });

  private gymBase = () => this.base()('uicons_gym');
  private pkmnBase = () => this.base()('uicons_pkmn');
  private raidBase = () => this.base()('uicons_raid');
  private rewardBase = () => this.base()('uicons_reward');
  private typeBase = () => this.base()('uicons_type');

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

  getTypeUrl(typeName: string): string {
    const id = POKEMON_TYPE_IDS[typeName];
    return id ? `${this.typeBase()}/${id}.png` : '';
  }
}
