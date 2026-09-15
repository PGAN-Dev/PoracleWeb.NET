import { ICON_SOURCE_FOLDERS, ICON_SOURCE_KEYS, IconSourceKey } from '../../core/services/icon.service';

/** One entry in the admin page's pack picker. */
export interface IconRepo {
  /** The pack root, with no trailing slash. Category folders hang off it. */
  base: string;
  /** What the card is labelled. Operator's words, not translated. */
  name: string;
}

/**
 * The setting holding the pack list. Admin-only: `IconService` reads the five `uicons_*` bases, not
 * this, so a non-admin never needs it and the settings allowlist does not carry it.
 */
export const ICON_REPO_SETTING_KEY = 'icon_repos';

/** More than anyone will use, and few enough that the page stays a page. */
export const MAX_ICON_REPOS = 25;

/**
 * What the picker offers before an operator edits the list.
 *
 * Whitewillem (Ingame) used to head this list and is gone: `whitewillem/PogoAssets` no longer exists
 * on GitHub. That it was hardcoded here, and that removing it needed a release, is the reason #877
 * asks for the list to be editable at all.
 */
export const DEFAULT_ICON_REPOS: readonly IconRepo[] = [
  { name: 'Nileplumb (Home)', base: 'https://raw.githubusercontent.com/nileplumb/PkmnHomeIcons/master/UICONS' },
  { name: 'Nileplumb (Shuffle)', base: 'https://raw.githubusercontent.com/nileplumb/PkmnShuffleMap/master/UICONS' },
  { name: 'Jms412 (Home)', base: 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS' },
  { name: 'Jms412 (Pokedex)', base: 'https://raw.githubusercontent.com/jms412/PkmnPokedexIcons/master/UICONS' },
];

/** The thumbnails on every pack card. One list, not a copy per entry. */
export const ICON_REPO_PREVIEWS: readonly { name: string; path: string }[] = [
  { name: 'Pikachu', path: 'pokemon/25.png' },
  { name: 'Charizard', path: 'pokemon/6.png' },
  { name: 'Mewtwo', path: 'pokemon/150.png' },
  { name: 'T5 Egg', path: 'raid/egg/5.png' },
  { name: 'Mystic', path: 'gym/1.png' },
];

/**
 * One file per category, proving the pack can actually dress this site.
 *
 * Derived from `ICON_SOURCE_FOLDERS` rather than listed separately, so a category added to
 * `IconService` cannot be left unprobed. `reward` and `raid` name a file one level down because that
 * is where this app reads them from -- `getItemUrl` builds `reward/item/`, `getRaidEggUrl` builds
 * `raid/egg/` -- and a pack with an empty `reward/` would otherwise pass.
 */
const PROBE_FILE: Readonly<Record<IconSourceKey, string>> = {
  uicons_raid: 'egg/5.png',
  uicons_gym: '1.png',
  uicons_invasion: '0.png',
  uicons_pkmn: '25.png',
  uicons_reward: 'item/1.png',
  uicons_type: '12.png',
};

/** Each category's probe path, relative to the pack root. */
export const ICON_REPO_PROBES: readonly { key: IconSourceKey; path: string }[] = ICON_SOURCE_KEYS.map(key => ({
  key,
  path: `${ICON_SOURCE_FOLDERS[key]}/${PROBE_FILE[key]}`,
}));

/** Trim, drop any trailing slashes. A base with one produces `.../UICONS//pokemon/25.png`. */
export function normalizeRepoBase(raw: string): string {
  return raw.trim().replace(/\/+$/, '');
}

/** True for an absolute http(s) URL and nothing else -- no `javascript:`, no relative path. */
export function isValidRepoBase(raw: string): boolean {
  let url: URL;
  try {
    url = new URL(normalizeRepoBase(raw));
  } catch {
    return false;
  }
  return url.protocol === 'http:' || url.protocol === 'https:';
}

/**
 * The stored list, or the built-in one when nothing is stored.
 *
 * An operator who removes every entry stores `[]`, which is not the same as having stored nothing:
 * an empty list stays empty, and only an absent or unreadable value falls back to the defaults. A
 * value this cannot read falls back rather than throwing, because an unreadable row must not take
 * the admin page down with it.
 */
export function parseIconRepos(raw: null | string | undefined): IconRepo[] {
  if (raw === null || raw === undefined || raw.trim() === '') return DEFAULT_ICON_REPOS.map(r => ({ ...r }));

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return DEFAULT_ICON_REPOS.map(r => ({ ...r }));
  }
  if (!Array.isArray(parsed)) return DEFAULT_ICON_REPOS.map(r => ({ ...r }));

  const seen = new Set<string>();
  const repos: IconRepo[] = [];
  for (const entry of parsed) {
    if (typeof entry !== 'object' || entry === null) continue;
    const { name, base } = entry as Partial<IconRepo>;
    if (typeof base !== 'string' || typeof name !== 'string') continue;

    const normalized = normalizeRepoBase(base);
    if (!isValidRepoBase(normalized) || !name.trim() || seen.has(normalized)) continue;

    seen.add(normalized);
    repos.push({ name: name.trim(), base: normalized });
    if (repos.length === MAX_ICON_REPOS) break;
  }
  return repos;
}

export function serializeIconRepos(repos: readonly IconRepo[]): string {
  return JSON.stringify(repos.map(({ name, base }) => ({ name, base })));
}

/** A label for a pack that is configured but not in the list -- its host and last path segment. */
export function describeRepoBase(base: string): string {
  try {
    const url = new URL(base);
    const last = url.pathname.split('/').filter(Boolean).slice(-2).join('/');
    return last ? `${url.host}/${last}` : url.host;
  } catch {
    return base;
  }
}
