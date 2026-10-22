import { Injectable, Injector, computed, inject, signal } from '@angular/core';

import { UserGeofenceService } from './user-geofence.service';
import { titleCaseArea } from '../../shared/utils/alarm-scope';
import { UserGeofence } from '../models';

/**
 * What to call an area the reader sees in a chip, a list or a summary.
 *
 * Stored area names are keys: lowercase, and for a drawn fence suffixed on a collision ("lake eola loop 2").
 * The person named it "Lake Eola Loop". Admin areas have no better name than their key, so they keep the
 * title-cased key. Display only: every request, mute value and comparison keeps the stored key.
 *
 * The caller's own fences are read once, on first use. A read that fails (or a context with no HTTP at all)
 * leaves the title-cased key in place, which is what was shown before.
 */
@Injectable({ providedIn: 'root' })
export class AreaNameService {
  private readonly injector = inject(Injector);
  private readonly loaded = signal<Map<string, string>>(new Map());
  private requested = false;

  /** Stored key, lowercased, to the name the person gave the fence. */
  readonly names = computed(() => this.loaded());

  constructor() {
    this.load();
  }

  /** The name to show for a stored area name. */
  label(area: string): string {
    return this.names().get(area.toLowerCase()) ?? titleCaseArea(area);
  }

  /** Feeds fences a host has already read, so it needn't be read twice. */
  learn(fences: Pick<UserGeofence, 'displayName' | 'kojiName'>[]): void {
    const next = new Map(this.loaded());
    for (const fence of fences) {
      if (fence.displayName && fence.kojiName) next.set(fence.kojiName.toLowerCase(), fence.displayName);
    }
    this.loaded.set(next);
  }

  private load(): void {
    if (this.requested) return;
    this.requested = true;

    try {
      this.injector
        .get(UserGeofenceService)
        .getCustomGeofences()
        .subscribe({
          error: () => undefined,
          next: own => this.learn(own ?? []),
        });
    } catch {
      // No HTTP in this context: keep the stored names.
    }
  }
}
