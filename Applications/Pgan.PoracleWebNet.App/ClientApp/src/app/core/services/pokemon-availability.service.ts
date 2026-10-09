import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';

import { ConfigService } from './config.service';
import { TokenStoreService } from './token-store.service';

interface PokemonAvailabilityResponse {
  available: number[];
  enabled: boolean;
}

const REFRESH_INTERVAL_MS = 300_000;

@Injectable({ providedIn: 'root' })
export class PokemonAvailabilityService {
  private readonly config = inject(ConfigService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly http = inject(HttpClient);
  private loaded = false;

  private readonly tokenStore = inject(TokenStoreService);
  readonly availableIds = signal<Set<number>>(new Set());
  readonly enabled = signal(false);
  readonly loading = signal(false);

  getAvailableCount(): number {
    return this.availableIds().size;
  }

  isAvailable(id: number): boolean {
    return this.availableIds().has(id);
  }

  load(): void {
    if (this.loaded) return;
    this.loaded = true;

    this.fetch();

    // A root service outlives the session, and so did this timer: it kept asking after Logout, for an
    // answer that could only be a 401.
    const intervalId = setInterval(() => {
      if (this.tokenStore.getAccessToken()) this.fetch();
    }, REFRESH_INTERVAL_MS);
    this.destroyRef.onDestroy(() => clearInterval(intervalId));
  }

  private fetch(): void {
    this.loading.set(true);

    const wasEnabled = this.enabled();

    this.http
      .get<PokemonAvailabilityResponse>(`${this.config.apiHost}/api/pokemon-availability`)
      .pipe(catchError(() => of({ available: [] as number[], enabled: wasEnabled })))
      .subscribe(res => {
        this.enabled.set(res.enabled);
        this.availableIds.set(new Set(res.available));
        this.loading.set(false);
      });
  }
}
