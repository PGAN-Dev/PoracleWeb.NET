import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';

/** One admin area, with whether it is currently off the menu. */
export interface AdminArea {
  group: string;
  /** Hidden by this site's own list. Editable here. */
  hidden: boolean;
  /**
   * Already non-selectable in Koji itself. Reported so the page can explain why an area is off the
   * menu for a reason this page did not cause and cannot undo.
   */
  hiddenInKoji: boolean;
  name: string;
}

export interface AdminAreaList {
  areas: AdminArea[];
  /** Hidden names Koji no longer serves. Kept rather than pruned, so an outage is not a deletion. */
  orphaned: string[];
}

/**
 * Reads admin areas for the hide toggle.
 *
 * Deliberately not `GET /api/areas/available`: that endpoint drops anything marked
 * `userSelectable: false` for every caller, admins included, so hiding through it would be one-way
 * and nothing could be un-hidden. See #885.
 */
@Injectable({ providedIn: 'root' })
export class AdminAreaService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  getAreas(): Observable<AdminAreaList> {
    return this.http.get<AdminAreaList>(`${this.config.apiHost}/api/admin/areas`);
  }

  /** Replaces the hidden list wholesale. `reloaded` is false when Poracle did not pick it up yet. */
  setHidden(names: string[]): Observable<{ reloaded: boolean; saved: boolean }> {
    return this.http.put<{ reloaded: boolean; saved: boolean }>(`${this.config.apiHost}/api/admin/areas`, { names });
  }
}
