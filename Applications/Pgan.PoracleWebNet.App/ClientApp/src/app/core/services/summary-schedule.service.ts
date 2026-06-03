import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';

import { ConfigService } from './config.service';
import { ActiveHourEntry, parseActiveHours } from '../models/active-hours.models';

export interface SummarySchedule {
  activeHours: ActiveHourEntry[];
  alertType: string; // 'quest' only today
}

interface CapabilityResponse {
  enabled: boolean;
}

interface SummaryScheduleResponse {
  activeHours: string;
  alertType: string;
}

const REFRESH_INTERVAL_MS = 300_000;

@Injectable({ providedIn: 'root' })
export class SummaryScheduleService {
  private readonly config = inject(ConfigService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly http = inject(HttpClient);

  private loaded = false;
  readonly enabled = signal(false); // false => hide panel + annotate hint

  private get base(): string {
    return `${this.config.apiHost}/api/summary-schedules`;
  }

  deleteSchedule(alertType: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${alertType}`);
  }

  getSchedule(alertType: string): Observable<SummarySchedule | null> {
    return this.http.get<SummaryScheduleResponse>(`${this.base}/${alertType}`).pipe(
      map(res => this.mapSchedule(res)),
      catchError(() => of(null)),
    );
  }

  getSchedules(): Observable<SummarySchedule[]> {
    return this.http.get<SummaryScheduleResponse[]>(this.base).pipe(map(list => list.map(res => this.mapSchedule(res))));
  }

  loadCapability(): void {
    if (this.loaded) return;
    this.loaded = true;

    this.fetchCapability();

    const intervalId = setInterval(() => this.fetchCapability(), REFRESH_INTERVAL_MS);
    this.destroyRef.onDestroy(() => clearInterval(intervalId));
  }

  setSchedule(alertType: string, hours: ActiveHourEntry[] | null): Observable<void> {
    return this.http.put<void>(`${this.base}/${alertType}`, { activeHours: JSON.stringify(hours ?? []) });
  }

  trigger(alertType: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${alertType}/trigger`, {});
  }

  private fetchCapability(): void {
    const wasEnabled = this.enabled();

    this.http
      .get<CapabilityResponse>(`${this.base}/capability`)
      .pipe(catchError(() => of({ enabled: wasEnabled })))
      .subscribe(res => this.enabled.set(res.enabled));
  }

  private mapSchedule(res: SummaryScheduleResponse): SummarySchedule {
    return {
      activeHours: parseActiveHours(res.activeHours),
      alertType: res.alertType,
    };
  }
}
