import { Injectable, signal } from '@angular/core';

/** Location mode applied to a newly created alarm: geofence areas, or a radius from the user's location. */
export type AlertLocationMode = 'areas' | 'distance';

/** Smallest default radius the preference UI accepts (km). */
export const MIN_DEFAULT_DISTANCE_KM = 0.1;

/** Largest default radius the preference UI accepts (km). */
export const MAX_DEFAULT_DISTANCE_KM = 100;

const MODE_KEY = 'poracle-default-alert-mode';
const DISTANCE_KEY = 'poracle-default-alert-distance-km';

/**
 * Client-side preference for how brand-new alarms default their delivery scope.
 *
 * Poracle fires an alarm either by the user's subscribed Areas (`distance = 0`) or by a radius
 * from their location (`distance > 0`). New alarms have always defaulted to Areas; this service
 * lets a user flip that default to Distance and pin a preferred radius, so the add dialogs open
 * pre-set instead of forcing the same two clicks every time. Persisted to localStorage, mirroring
 * the theme/accent/language preference pattern (see {@link https://github.com/PGAN-Dev/PoracleWeb.NET/discussions/217}).
 */
@Injectable({ providedIn: 'root' })
export class AlertDefaultsService {
  /** Preferred radius (km) used to seed new distance-mode alarms. Always within [min, max]. */
  readonly defaultDistanceKm = signal<number>(this.readDistanceKm());

  /** Preferred location mode used to seed new alarms. */
  readonly defaultMode = signal<AlertLocationMode>(this.readMode());

  /** Persist the user's preferred defaults for new alarms. */
  save(mode: AlertLocationMode, distanceKm: number): void {
    const km = this.clampDistance(distanceKm);
    this.defaultMode.set(mode);
    this.defaultDistanceKm.set(km);
    localStorage.setItem(MODE_KEY, mode);
    localStorage.setItem(DISTANCE_KEY, String(km));
  }

  private clampDistance(value: number): number {
    if (!Number.isFinite(value) || value <= 0) return 1;
    return Math.min(MAX_DEFAULT_DISTANCE_KM, Math.max(MIN_DEFAULT_DISTANCE_KM, value));
  }

  private readDistanceKm(): number {
    const raw = Number(localStorage.getItem(DISTANCE_KEY));
    return Number.isFinite(raw) && raw > 0 ? this.clampDistance(raw) : 1;
  }

  private readMode(): AlertLocationMode {
    return localStorage.getItem(MODE_KEY) === 'distance' ? 'distance' : 'areas';
  }
}
