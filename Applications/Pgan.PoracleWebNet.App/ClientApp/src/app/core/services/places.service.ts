import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { ConfigService } from './config.service';
import { pinOrNull } from '../../shared/utils/location.utils';
import { SavedPlace, SavedPlaces } from '../models';

/**
 * The places a user can point an alarm at.
 *
 * Held as a signal because the Where sheet, the Places screen and every alarm card read the same
 * list, and a place added in one has to show up in the others without a reload.
 */
@Injectable({ providedIn: 'root' })
export class PlacesService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private readonly places = signal<null | SavedPlaces>(null);

  /**
   * Whether this Poracle server can move a place without deleting it first. Needs PoracleNG 5.2.0;
   * before it, a place an alarm referenced could not be moved at all, because the delete answers 409
   * while anything still points at it. False until {@link load} has run.
   */
  readonly canEdit = computed(() => this.places()?.canEdit === true);

  /** Named places only. Empty until {@link load} has run. */
  readonly named = computed(() => this.places()?.named ?? []);

  /** The profile pin, which every alarm falls back to. */
  /** The profile pin, or null when it is the 0,0 Poracle stores for "not set". */
  readonly pin = computed(() => pinOrNull(this.places()?.default));

  add(place: SavedPlace): Observable<SavedPlaces> {
    return this.http.post<SavedPlaces>(`${this.config.apiHost}/api/location/places`, place).pipe(tap(updated => this.places.set(updated)));
  }

  load(): Observable<SavedPlaces> {
    return this.http.get<SavedPlaces>(`${this.config.apiHost}/api/location/places`).pipe(tap(places => this.places.set(places)));
  }

  /**
   * Moves a place, keeping its label so every alarm pointing at it follows. Answers 501 on a server
   * without the endpoint, which is why {@link canEdit} gates the control that calls this.
   */
  move(label: string, latitude: number, longitude: number): Observable<SavedPlaces> {
    return this.http
      .put<SavedPlaces>(`${this.config.apiHost}/api/location/places/${encodeURIComponent(label)}`, { latitude, longitude })
      .pipe(tap(updated => this.places.set(updated)));
  }

  /**
   * Deletes a place. Answers 409 with `referencingRules` when alarms still point at it — the caller
   * should name them rather than reporting a bare failure.
   */
  remove(label: string): Observable<void> {
    return this.http
      .delete<void>(`${this.config.apiHost}/api/location/places/${encodeURIComponent(label)}`)
      .pipe(
        tap(() => this.places.update(current => (current ? { ...current, named: current.named.filter(p => p.label !== label) } : current))),
      );
  }
}
