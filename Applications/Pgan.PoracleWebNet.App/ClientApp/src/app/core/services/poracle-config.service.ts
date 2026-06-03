import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, ReplaySubject, catchError, of, tap } from 'rxjs';

import { ConfigService } from './config.service';
import { PoracleServerConfig } from '../models';

const FALLBACK: PoracleServerConfig = {
  providerURL: '',
  defaultPvpCap: 0,
  defaultTemplateName: 'default',
  everythingFlagPermissions: '',
  locale: 'en',
  maxDistance: 10726000,
  poracleVersion: 'unknown',
  pvpCaps: [],
  pvpFilterGreatMinCp: 0,
  pvpFilterLittleMinCp: 0,
  pvpFilterMaxRank: 100,
  pvpFilterUltraMinCp: 0,
  pvpLittleLeagueAllowed: true,
  staticKey: '',
};

@Injectable({ providedIn: 'root' })
export class PoracleConfigService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private loadRequested = false;
  private readonly ready$ = new ReplaySubject<PoracleServerConfig>(1);

  readonly serverConfig = signal<PoracleServerConfig>(FALLBACK);

  load(): Observable<PoracleServerConfig> {
    if (!this.loadRequested) {
      this.loadRequested = true;
      this.http
        .get<PoracleServerConfig>(`${this.config.apiHost}/api/config`)
        .pipe(
          tap(cfg => {
            this.serverConfig.set({ ...FALLBACK, ...cfg });
            this.ready$.next(this.serverConfig());
          }),
          catchError(() => {
            this.ready$.next(FALLBACK);
            return of(FALLBACK);
          }),
        )
        .subscribe();
    }
    return this.ready$.asObservable();
  }
}
