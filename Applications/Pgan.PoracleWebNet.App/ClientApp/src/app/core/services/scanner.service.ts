import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

export interface GymSearchResult {
  area: string | null;
  id: string;
  lat: number;
  lon: number;
  name: string | null;
  teamId: number | null;
  url: string | null;
}

@Injectable({ providedIn: 'root' })
export class ScannerService {
  private readonly http = inject(HttpClient);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  getGymById(id: string): Observable<GymSearchResult | null> {
    return this.http
      .get<GymSearchResult>(`/api/scanner/gyms/${encodeURIComponent(id)}`)
      .pipe(catchError((err: HttpErrorResponse) => this.handleError(err, null)));
  }

  getMaxBattlePokemonIds(): Observable<number[]> {
    return this.http
      .get<number[]>('/api/scanner/max-battle-pokemon')
      .pipe(catchError((err: HttpErrorResponse) => this.handleError<number[]>(err, [])));
  }

  searchGyms(search: string, limit = 20): Observable<GymSearchResult[]> {
    if (search.length < 2) return of([]);
    return this.http
      .get<GymSearchResult[]>('/api/scanner/gyms', { params: { limit, search } })
      .pipe(catchError((err: HttpErrorResponse) => this.handleError<GymSearchResult[]>(err, [])));
  }

  private handleError<T>(err: HttpErrorResponse, fallback: T): Observable<T> {
    if (err.status === 429) {
      // Raw English in all eleven locales, in a service whose neighbours all translate. See #619.
      this.snackBar.open(this.translate.instant('GYM_PICKER.RATE_LIMITED'), this.translate.instant('TOAST.OK'), {
        duration: 4000,
      });
    }
    return of(fallback);
  }
}
