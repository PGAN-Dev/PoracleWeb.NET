import { TranslateService } from '@ngx-translate/core';

import { AlarmScope, AlarmScopeMode } from '../../core/models';

export type { AlarmScope, AlarmScopeMode };

/**
 * The three answers an alarm can give to "where should this reach me", read off the two override
 * fields and the radius.
 *
 * PoracleNG stores them as independent columns and enforces their mutual exclusion with three
 * validation rules. Reading them back into one discriminated value is what lets the UI offer a radio
 * group instead of three fields plus error messages, so the invalid combinations cannot be expressed.
 */
export function scopeOf(
  overrideLocationLabel: null | string | undefined,
  overrideAreas: null | string[] | undefined,
  distanceMetres: number,
): AlarmScope {
  if (overrideAreas && overrideAreas.length > 0) {
    return { areas: overrideAreas, mode: 'areas' };
  }

  if (overrideLocationLabel) {
    return {
      distanceKm: metresToKm(distanceMetres),
      mode: 'place',
      placeLabel: overrideLocationLabel,
    };
  }

  // A radius with no place is the behaviour that predates per-alarm scope: measured from the profile
  // pin. It has to survive as its own reading, or "within 500 m of me" renders as "anywhere in my
  // areas" — the same words for the opposite of what the alarm does. No radius is no distanceKm at all,
  // so a profile scope that carries one, even 0 from an emptied field, always means "near a point".
  return distanceMetres > 0 ? { distanceKm: metresToKm(distanceMetres), mode: 'profile' } : { mode: 'profile' };
}

/**
 * The scope as the fields PoracleNG stores. The unused half is sent as an explicit empty rather than
 * null, because null means "not stated, keep what is stored" on the write path — an override could
 * otherwise be set but never taken off.
 */
export function scopeToFields(scope: AlarmScope): {
  distance: number;
  overrideAreas: string[];
  overrideLocationLabel: string;
} {
  switch (scope.mode) {
    case 'areas':
      // Areas and a radius are mutually exclusive upstream, so the radius goes to zero here rather
      // than being left at whatever the form last held.
      return { overrideAreas: scope.areas ?? [], overrideLocationLabel: '', distance: 0 };
    case 'place':
      return {
        overrideAreas: [],
        overrideLocationLabel: scope.placeLabel ?? '',
        distance: kmToMetres(scope.distanceKm ?? 0),
      };
    default:
      // Inherited scope, with or without a radius from the pin. Both overrides are cleared explicitly.
      return { overrideAreas: [], overrideLocationLabel: '', distance: kmToMetres(scope.distanceKm ?? 0) };
  }
}

/** One line describing the scope, in the second person, for a chip or a summary row. */
export function describeScope(scope: AlarmScope, profileAreas: string[], translate: TranslateService): string {
  switch (scope.mode) {
    case 'areas': {
      const areas = scope.areas ?? [];
      return translate.instant('WHERE.ONLY_IN', { areas: formatAreaList(areas, translate) });
    }
    case 'place':
      return translate.instant('WHERE.NEAR_PLACE', {
        distance: formatDistance(scope.distanceKm ?? 0),
        place: scope.placeLabel ?? '',
      });
    default:
      if ((scope.distanceKm ?? 0) > 0) {
        return translate.instant('WHERE.NEAR_PIN', { distance: formatDistance(scope.distanceKm ?? 0) });
      }

      return profileAreas.length > 0 ? translate.instant('WHERE.PROFILE_AREAS') : translate.instant('WHERE.PROFILE_ANYWHERE');
  }
}

/**
 * Area names as a reader would say them. Past three the list stops being informative and starts being
 * a wall, so the tail is counted instead.
 */
export function formatAreaList(areas: string[], translate: TranslateService): string {
  const shown = areas.slice(0, 3).map(titleCaseArea);

  return areas.length > 3
    ? translate.instant('WHERE.AREA_LIST_MORE', { areas: shown.join(', '), count: areas.length - 3 })
    : shown.join(', ');
}

/** Geofence names are stored lowercase because Poracle matches case-sensitively; people are not. */
export function titleCaseArea(area: string): string {
  return area
    .split(' ')
    .map(word => (word.length > 0 ? word[0].toUpperCase() + word.slice(1) : word))
    .join(' ');
}

/**
 * As many decimals as the radius holds, down to whole metres: "2" for 2 km, "0.04" for a stored 40 m.
 * Trailing zeroes read as false precision, and rounding to a tenth showed a 40 m radius as "0.0".
 */
export function formatDistance(km: number): string {
  return `${Math.round(km * 1000) / 1000}`;
}

/** A km figure in the display language's number format, for messages that quote a bound ("0,1" in German). */
export function formatKm(km: number, lang: string): string {
  try {
    return new Intl.NumberFormat(lang === 'pt' ? 'pt-PT' : lang || 'en', { maximumFractionDigits: 3 }).format(km);
  } catch {
    return formatDistance(km);
  }
}

/**
 * The radius range the alarm dialogs offer. It's the input's range, not a product limit: the API takes
 * anything up to half the Earth's circumference (`AlarmDistance.MaxMetres`), and production holds rules
 * far past 100 km.
 */
export const MIN_RADIUS_KM = 0.1;
export const MAX_RADIUS_KM = 100;

/** The API's own cap, `AlarmDistance.MaxMetres`: half the Earth's circumference. */
export const MAX_RADIUS_METRES = 20_037_509;

/**
 * Whether a radius typed into a dialog may be saved. A stored radius outside the range is accepted while
 * it is left unchanged, so an edit to some other field of such a rule still saves. A stored zero is no
 * radius at all, so it earns no exemption.
 */
export function radiusAcceptable(km: null | number | undefined, storedKm?: null | number): boolean {
  if (km === null || km === undefined || !Number.isFinite(km)) return false;
  if (storedKm !== null && storedKm !== undefined && storedKm > 0 && km === storedKm) return true;

  return km >= MIN_RADIUS_KM && km <= MAX_RADIUS_KM;
}

/**
 * The bulk radius dialog edits existing rules, so it takes what the server takes rather than the add
 * dialogs' range: production holds rules at 10,000 km, and no screen may lose the ability to set one.
 * Above zero once rounded to whole metres, and no further than `AlarmDistance.MaxMetres`.
 */
export function bulkRadiusAcceptable(km: null | number | undefined): boolean {
  if (km === null || km === undefined || !Number.isFinite(km)) return false;
  const metres = kmToMetres(km);

  return metres >= 1 && metres <= MAX_RADIUS_METRES;
}

/**
 * Exact, not rounded. Rounding to a tenth of a km rewrote every stored radius on the next save of any
 * other field: 250 m became 300 m, and a pin radius under 50 m became 0, an areas rule.
 */
export function metresToKm(metres: number): number {
  return metres / 1000;
}

export function kmToMetres(km: number): number {
  return Math.round(km * 1000);
}
