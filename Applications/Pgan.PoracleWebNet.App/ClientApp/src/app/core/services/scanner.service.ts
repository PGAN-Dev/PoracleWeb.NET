import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
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

  getGymById(id: string): Observable<GymSearchResult | null> {
    return this.http.get<GymSearchResult>(`/api/scanner/gyms/${encodeURIComponent(id)}`).pipe(catchError(() => of(null)));
  }

  getMaxBattlePokemonIds(): Observable<number[]> {
    return this.http.get<number[]>('/api/scanner/max-battle-pokemon').pipe(catchError(() => of([])));
  }

  searchGyms(search: string, limit = 20): Observable<GymSearchResult[]> {
    if (search.length < 2) return of([]);
    return this.http.get<GymSearchResult[]>('/api/scanner/gyms', { params: { limit, search } }).pipe(catchError(() => of([])));
  }
}
