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

  return { mode: 'profile' };
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
      return { overrideAreas: [], overrideLocationLabel: '', distance: 0 };
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

/** Trailing zeroes read as false precision on a radius someone typed as "2". */
export function formatDistance(km: number): string {
  return Number.isInteger(km) ? `${km}` : `${km.toFixed(1)}`;
}

export function metresToKm(metres: number): number {
  return Math.round((metres / 1000) * 10) / 10;
}

export function kmToMetres(km: number): number {
  return Math.round(km * 1000);
}
