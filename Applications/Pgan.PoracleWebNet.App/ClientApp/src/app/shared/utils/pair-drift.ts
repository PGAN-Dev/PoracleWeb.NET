import { DriftField, DriftItem, LinkSide, ReverseGeocodingResult } from '../../core/models';

/** Navigation state saying Linked accounts was opened to review the differences, so it takes focus on arrival. */
export const DRIFT_REVIEW_STATE = 'driftReview';

export const DRIFT_SIDES: readonly LinkSide[] = ['discord', 'telegram'];

export interface DriftPoint {
  lat: number;
  lon: number;
}

/** What the panel draws for one difference: a label, what differs, and what each choice does to the other app. */
export interface DriftLine {
  /** Keeping that side can't work yet (the other app lacks the profile to switch to); the consequence says why. */
  blocked: Record<LinkSide, boolean>;
  /** What keeping that side does to the other app, said before anything happens. */
  consequence: Record<LinkSide, string>;
  item: DriftItem;
  labelKey: string;
  /** What keeping that side deletes on the other app: profiles or saved places. Non-empty means confirm first. */
  removes: Record<LinkSide, string[]>;
  text: string;
}

export interface DriftDescribeContext {
  /** The name to show for a stored area key. */
  area: (key: string) => string;
  /** The reverse-geocode answer for a point, once it has arrived. */
  geo: (point: DriftPoint) => ReverseGeocodingResult | undefined;
  /** The display language, for number formatting. */
  lang: string;
  side: (side: LinkSide) => string;
  t: (key: string, params?: Record<string, unknown>) => string;
}

interface ProfileRow {
  active_hours?: unknown;
  areas?: unknown;
  lat?: unknown;
  lon?: unknown;
  name?: string;
  no: number;
}

interface PlaceRow {
  label: string;
  lat?: number;
  lon?: number;
}

const LABEL_KEYS: Record<DriftField, string> = {
  areas: 'DRIFT.LABEL_AREAS',
  current_profile: 'DRIFT.LABEL_CURRENT_PROFILE',
  location: 'DRIFT.LABEL_LOCATION',
  places: 'DRIFT.LABEL_PLACES',
  profiles: 'DRIFT.LABEL_PROFILES',
};

/** The success line for each field, said once the other app matches. */
export const RESOLVED_KEYS: Record<DriftField, string> = {
  areas: 'DRIFT.RESOLVED_AREAS',
  current_profile: 'DRIFT.RESOLVED_CURRENT_PROFILE',
  location: 'DRIFT.RESOLVED_LOCATION',
  places: 'DRIFT.RESOLVED_PLACES',
  profiles: 'DRIFT.RESOLVED_PROFILES',
};

export function otherSide(side: LinkSide): LinkSide {
  return side === 'discord' ? 'telegram' : 'discord';
}

export function isDriftPoint(value: unknown): value is DriftPoint {
  const p = value as DriftPoint | null;
  return typeof p?.lat === 'number' && typeof p?.lon === 'number';
}

export function pointKey(p: DriftPoint): string {
  return `${p.lat},${p.lon}`;
}

/** The server's area comparison key (PoracleNG's NormalizeAreaKey): lowercase, trimmed, `_` equal to space. */
export function areaKey(name: string): string {
  return name.trim().toLowerCase().replace(/_/g, ' ');
}

/** Great-circle distance in km. */
export function distanceKm(a: DriftPoint, b: DriftPoint): number {
  const rad = (d: number) => (d * Math.PI) / 180;
  const dLat = rad(b.lat - a.lat);
  const dLon = rad(b.lon - a.lon);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLon / 2) ** 2;
  return 2 * 6371.0088 * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** PoracleNG's "no location": 0,0. */
function unset(p: DriftPoint): boolean {
  return p.lat === 0 && p.lon === 0;
}

/** The levels a reverse-geocode answer names, finest first. Both sides are named at the same one. */
function levels(r: ReverseGeocodingResult | undefined): (string | undefined)[] {
  return [r?.city || r?.town || r?.village, r?.county, r?.state, r?.country];
}

/**
 * Both places at the finest level both answers name, so one side is never a town and the other a country. Null when
 * there is no shared level (one lookup failed or is still on its way).
 */
export function sameLevelNames(a: ReverseGeocodingResult | undefined, b: ReverseGeocodingResult | undefined): [string, string] | null {
  const la = levels(a);
  const lb = levels(b);
  for (let i = 0; i < la.length; i++) {
    const x = la[i];
    const y = lb[i];
    if (x && y) return [x, y];
  }
  return null;
}

function formatDistance(km: number, lang: string): string {
  const digits = km < 10 ? 1 : 0;
  try {
    return new Intl.NumberFormat(lang === 'pt' ? 'pt-PT' : lang || 'en', { maximumFractionDigits: digits }).format(km);
  } catch {
    return km.toFixed(digits);
  }
}

function list<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function stringList(value: unknown): string[] {
  return list<unknown>(value).filter((v): v is string => typeof v === 'string');
}

function join(names: string[]): string {
  return names.join(', ');
}

/** A list read the way the language reads one ("name, schedule and areas"). */
function joinList(items: string[], lang: string): string {
  try {
    return new Intl.ListFormat(lang === 'pt' ? 'pt-PT' : lang || 'en', { style: 'long', type: 'conjunction' }).format(items);
  } catch {
    return items.join(', ');
  }
}

function bySides<T>(make: (side: LinkSide) => T): Record<LinkSide, T> {
  return { discord: make('discord'), telegram: make('telegram') };
}

/** Sentences joined with a space, the empty ones dropped. */
function sentences(parts: string[]): string {
  return parts.filter(p => p.length > 0).join(' ');
}

export function describeDrift(item: DriftItem, ctx: DriftDescribeContext): DriftLine {
  const base = { item, labelKey: LABEL_KEYS[item.field] ?? 'DRIFT.LABEL_OTHER' };
  const none = bySides<string[]>(() => []);
  const free = bySides(() => false);
  switch (item.field) {
    case 'areas':
      return { ...base, ...describeAreas(item, ctx), blocked: free, removes: none };
    case 'current_profile':
      return { ...base, ...describeCurrentProfile(item, ctx), removes: none };
    case 'location':
      return { ...base, ...describeLocation(item, ctx), blocked: free, removes: none };
    case 'places':
      return { ...base, ...describePlaces(item, ctx), blocked: free };
    case 'profiles':
      return { ...base, ...describeProfiles(item, ctx), blocked: free };
    default:
      return { ...base, blocked: free, consequence: bySides(() => ''), removes: none, text: '' };
  }
}

function describeAreas(item: DriftItem, ctx: DriftDescribeContext): Pick<DriftLine, 'consequence' | 'text'> {
  const only = bySides(side => {
    const theirs = new Set(stringList(item[otherSide(side)]).map(areaKey));
    const seen = new Set<string>();
    return stringList(item[side]).filter(a => {
      const key = areaKey(a);
      if (theirs.has(key) || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  });
  const names = bySides(side => only[side].map(a => ctx.area(a)));
  const text = sentences(
    DRIFT_SIDES.filter(s => names[s].length > 0).map(s => ctx.t('DRIFT.AREAS_EXTRA', { names: join(names[s]), side: ctx.side(s) })),
  );
  const consequence = bySides(keep => {
    const other = otherSide(keep);
    const gains = names[keep];
    const loses = names[other];
    return (
      sentences([
        gains.length ? ctx.t('DRIFT.CONSEQ_GETS', { names: join(gains), other: ctx.side(other) }) : '',
        loses.length ? ctx.t('DRIFT.CONSEQ_AREAS_LOSES', { names: join(loses), other: ctx.side(other) }) : '',
      ]) || ctx.t('DRIFT.CONSEQ_AREAS', { kept: ctx.side(keep), other: ctx.side(other) })
    );
  });
  return { consequence, text: text || ctx.t('DRIFT.AREAS_DIFFER') };
}

function describeCurrentProfile(item: DriftItem, ctx: DriftDescribeContext): Pick<DriftLine, 'blocked' | 'consequence' | 'text'> {
  const details = (item.details ?? {}) as Partial<
    Record<LinkSide, { name?: null | string; otherHas?: boolean; otherName?: null | string }>
  >;
  const nameOf = (side: LinkSide): null | string => {
    const named = details[side]?.name;
    if (typeof named === 'string' && named.trim()) return named;
    const no = item[side];
    return typeof no === 'number' || typeof no === 'string' ? ctx.t('DRIFT.PROFILE_NAMELESS', { no }) : null;
  };
  const discord = nameOf('discord');
  const telegram = nameOf('telegram');
  const text =
    discord !== null && telegram !== null ? ctx.t('DRIFT.PROFILE_CURRENT', { discord, telegram }) : ctx.t('DRIFT.PROFILE_CURRENT_DIFFER');
  // Unknown is not blocked: an older status without details lets the server answer.
  const blocked = bySides(side => details[side]?.otherHas === false);
  const consequence = bySides(keep => {
    const other = otherSide(keep);
    // The app being changed switches to its own profile at that number, so that is the name it will show.
    const own = details[keep]?.otherName;
    const name = (typeof own === 'string' && own.trim() ? own : nameOf(keep)) ?? '';
    return blocked[keep]
      ? ctx.t('DRIFT.CONSEQ_PROFILE_MISSING', { name, other: ctx.side(other) })
      : ctx.t('DRIFT.CONSEQ_SWITCH', { name, other: ctx.side(other) });
  });
  return { blocked, consequence, text };
}

function describeLocation(item: DriftItem, ctx: DriftDescribeContext): Pick<DriftLine, 'consequence' | 'text'> {
  const d = item.discord;
  const t = item.telegram;
  const consequence = bySides(keep => ctx.t('DRIFT.CONSEQ_LOCATION', { kept: ctx.side(keep), other: ctx.side(otherSide(keep)) }));
  if (!isDriftPoint(d) || !isDriftPoint(t)) return { consequence, text: ctx.t('DRIFT.LOCATION_DIFFER') };
  if (unset(d) || unset(t)) {
    const set: LinkSide = unset(d) ? 'telegram' : 'discord';
    return { consequence, text: ctx.t('DRIFT.LOCATION_UNSET', { set: ctx.side(set), unset: ctx.side(otherSide(set)) }) };
  }
  const distance = formatDistance(distanceKm(d, t), ctx.lang);
  const names = sameLevelNames(ctx.geo(d), ctx.geo(t));
  if (names === null) return { consequence, text: ctx.t('DRIFT.LOCATION_APART', { distance }) };
  if (names[0] === names[1]) return { consequence, text: ctx.t('DRIFT.LOCATION_SAME_PLACE', { distance, place: names[0] }) };
  return { consequence, text: ctx.t('DRIFT.LOCATION', { discord: names[0], distance, telegram: names[1] }) };
}

function describePlaces(item: DriftItem, ctx: DriftDescribeContext): Pick<DriftLine, 'consequence' | 'removes' | 'text'> {
  const labels = ((item.details as { labels?: Record<string, string> } | undefined)?.labels ?? {}) as Record<string, string>;
  const show = (label: string) => labels[label] ?? label;
  const rows = bySides(side => list<PlaceRow>(item[side]).filter(r => typeof r?.label === 'string'));
  const only = bySides(side => rows[side].filter(r => !rows[otherSide(side)].some(o => o.label === r.label)).map(r => show(r.label)));
  const moved = rows.discord
    .filter(r => rows.telegram.some(o => o.label === r.label && (o.lat !== r.lat || o.lon !== r.lon)))
    .map(r => show(r.label));
  const text = sentences([
    ...DRIFT_SIDES.filter(s => only[s].length > 0).map(s => ctx.t('DRIFT.PLACES_EXTRA', { names: join(only[s]), side: ctx.side(s) })),
    moved.length ? ctx.t('DRIFT.PLACES_MOVED', { names: join(moved) }) : '',
  ]);
  const consequence = bySides(keep => {
    const other = otherSide(keep);
    return sentences([
      only[keep].length ? ctx.t('DRIFT.CONSEQ_GETS', { names: join(only[keep]), other: ctx.side(other) }) : '',
      moved.length ? ctx.t('DRIFT.CONSEQ_PLACES_MOVED', { names: join(moved), other: ctx.side(other) }) : '',
      only[other].length ? ctx.t('DRIFT.CONSEQ_DELETES', { names: join(only[other]), other: ctx.side(other) }) : '',
    ]);
  });
  // Keeping one side deletes the places only the other side has.
  const removes = bySides(keep => only[otherSide(keep)]);
  return { consequence, removes, text: text || ctx.t('DRIFT.PLACES_DIFFER') };
}

function describeProfiles(item: DriftItem, ctx: DriftDescribeContext): Pick<DriftLine, 'consequence' | 'removes' | 'text'> {
  const rows = bySides(side => list<ProfileRow>(item[side]).filter(r => typeof r?.no === 'number'));
  const nameOf = (r: ProfileRow) => (r.name && r.name.trim() ? r.name : ctx.t('DRIFT.PROFILE_NAMELESS', { no: r.no }));
  const only = bySides(side => rows[side].filter(r => !rows[otherSide(side)].some(o => o.no === r.no)).map(nameOf));

  const changed: string[] = [];
  const changedNos = new Set<number>();
  for (const d of rows.discord) {
    const t = rows.telegram.find(o => o.no === d.no);
    if (!t) continue;
    const what: string[] = [];
    if ((d.name ?? '') !== (t.name ?? '')) what.push(ctx.t('DRIFT.DIFF_NAME'));
    if (JSON.stringify(d.active_hours ?? []) !== JSON.stringify(t.active_hours ?? [])) what.push(ctx.t('DRIFT.DIFF_SCHEDULE'));
    if (JSON.stringify([d.areas, d.lat, d.lon]) !== JSON.stringify([t.areas, t.lat, t.lon])) what.push(ctx.t('DRIFT.DIFF_AREAS'));
    if (!what.length) continue;
    changedNos.add(d.no);
    const name =
      (d.name ?? '') === (t.name ?? '')
        ? nameOf(d)
        : ctx.t('DRIFT.PROFILE_PAIR', {
            discord: ctx.side('discord'),
            discordName: nameOf(d),
            telegram: ctx.side('telegram'),
            telegramName: nameOf(t),
          });
    changed.push(ctx.t('DRIFT.PROFILE_CHANGED_ONE', { name, what: joinList(what, ctx.lang) }));
  }

  const text = sentences([
    ...DRIFT_SIDES.filter(s => only[s].length > 0).map(s => ctx.t('DRIFT.PROFILES_EXTRA', { names: join(only[s]), side: ctx.side(s) })),
    changed.length ? ctx.t('DRIFT.PROFILES_CHANGED', { names: changed.join('; ') }) : '',
  ]);
  const current = (item.details ?? {}) as Partial<Record<LinkSide, { current?: unknown }>>;
  const consequence = bySides(keep => {
    const other = otherSide(keep);
    // Named as the app being changed calls them now, since that is where the person will look for them.
    const matched = rows[other].filter(r => changedNos.has(r.no)).map(nameOf);
    const switchTo = switchTarget(rows, current, keep);
    return sentences([
      only[keep].length ? ctx.t('DRIFT.CONSEQ_GETS', { names: join(only[keep]), other: ctx.side(other) }) : '',
      matched.length ? ctx.t('DRIFT.CONSEQ_PROFILES_MATCH', { names: join(matched), other: ctx.side(other) }) : '',
      only[other].length ? ctx.t('DRIFT.CONSEQ_DELETES', { names: join(only[other]), other: ctx.side(other) }) : '',
      switchTo ? ctx.t('DRIFT.CONSEQ_PROFILES_SWITCH', { name: nameOf(switchTo), other: ctx.side(other) }) : '',
    ]);
  });
  const removes = bySides(keep => only[otherSide(keep)]);
  return { consequence, removes, text: text || ctx.t('DRIFT.PROFILES_DIFFER') };
}

/**
 * The profile the other app is moved onto when keeping `keep` deletes the profile it is on, mirroring the server:
 * the kept side's active profile, or its lowest when that is profile 0. Null when nothing moves, or when the status
 * does not say which profiles are active.
 */
function switchTarget(
  rows: Record<LinkSide, ProfileRow[]>,
  current: Partial<Record<LinkSide, { current?: unknown }>>,
  keep: LinkSide,
): null | ProfileRow {
  const other = otherSide(keep);
  const otherCurrent = current[other]?.current;
  const keptCurrent = current[keep]?.current;
  if (typeof otherCurrent !== 'number' || typeof keptCurrent !== 'number') return null;
  if (rows[keep].some(r => r.no === otherCurrent)) return null;
  const target = keptCurrent !== 0 ? keptCurrent : Math.min(...rows[keep].map(r => r.no));
  return rows[keep].find(r => r.no === target) ?? null;
}
