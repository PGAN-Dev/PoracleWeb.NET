import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, of, take } from 'rxjs';

import { environment } from '../../../environments/environment';
import { KNOWN_LEVELS, LevelOption } from '../models/raid-level.models';

/** API payload shape from GET /api/masterdata/raid-levels. */
interface RaidLevelInfoDto {
  category: string;
  name: string;
  namePlural: string;
  value: number;
}

/**
 * Fetches the canonical raid-level list from the API on app load and caches
 * it in a signal. The hardcoded `KNOWN_LEVELS` constant acts as a fallback:
 * if the network call fails, or before it resolves, callers still get the
 * baked-in 19 levels. New levels appearing in the API response (raid_20+
 * once Niantic ships them) surface automatically without a frontend change.
 */
@Injectable({ providedIn: 'root' })
export class RaidLevelService {
  /** Hot signal of the current level list. Starts with the baked-in defaults. */
  private readonly _levels = signal<readonly LevelOption[]>(KNOWN_LEVELS);

  /** Returns true once the fetch has resolved (success OR failure). */
  private readonly _loaded = signal<boolean>(false);

  private readonly http = inject(HttpClient);

  /**
   * Lookup by value. Used by alarm cards/labels so the displayed name follows
   * the live list when the API extends it.
   */
  readonly byValue = computed(() => {
    const map = new Map<number, LevelOption>();
    for (const l of this._levels()) map.set(l.value, l);
    return map;
  });

  /** Reactive read-only handle for components. */
  readonly levels = this._levels.asReadonly();

  readonly loaded = this._loaded.asReadonly();

  /**
   * Kick off a one-time fetch. Safe to call multiple times — subsequent calls
   * are no-ops while a request is in flight or after one has succeeded.
   */
  load(): void {
    if (this._loaded()) return;
    this.http
      .get<RaidLevelInfoDto[]>(`${environment.apiUrl}/api/masterdata/raid-levels`)
      .pipe(
        take(1),
        catchError(() => of(null)),
      )
      .subscribe(dtos => {
        if (dtos && dtos.length > 0) {
          this._levels.set(dtos.map(toLevelOption));
        }
        this._loaded.set(true);
      });
  }
}

/**
 * Map the server-side DTO to the frontend `LevelOption`. We trust the integer
 * + category from the server; i18n keys are derived deterministically from the
 * value so translations stay in our locale files (the masterfile is English-only).
 * The server's `name` / `namePlural` are exposed as backup strings that the
 * label pipe can fall back to when an i18n key is missing.
 */
function toLevelOption(dto: RaidLevelInfoDto): LevelOption {
  return {
    category: normalizeCategory(dto.category),
    labelKey: `RAIDS.LEVEL.RAID_${dto.value}`,
    value: dto.value,
  };
}

function normalizeCategory(c: string): LevelOption['category'] {
  switch (c) {
    case 'star':
    case 'mega':
    case 'special':
    case 'shadow':
    case 'superMega':
    case 'coordinated':
    case 'any':
    case 'custom':
      return c;
    default:
      return 'custom';
  }
}
