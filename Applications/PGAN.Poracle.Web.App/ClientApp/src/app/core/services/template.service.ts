import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, ReplaySubject, map } from 'rxjs';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';

export interface TemplateData {
  discord: Record<string, Record<string, (string | number)[]>>;
  status: string;
  telegram: Record<string, Record<string, (string | number)[]>>;
}

@Injectable({ providedIn: 'root' })
export class TemplateService {
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private loaded = false;
  private templates$ = new ReplaySubject<TemplateData>(1);

  getTemplatesForType(alarmType: string): Observable<(string | number)[]> {
    return this.loadTemplates().pipe(
      map(data => {
        const userType = this.auth.user()?.type || 'discord:user';
        const platform = userType.startsWith('telegram') ? 'telegram' : 'discord';
        const typeData = data[platform]?.[alarmType];
        if (!typeData) return [];
        const templates = new Set<string | number>();
        Object.values(typeData).forEach((arr: unknown) => {
          if (Array.isArray(arr)) arr.forEach((t: string | number) => templates.add(t));
        });
        return [...templates];
      }),
    );
  }

  loadTemplates(): Observable<TemplateData> {
    if (!this.loaded) {
      this.loaded = true;
      this.http.get<TemplateData>(`${this.config.apiHost}/api/config/templates`).subscribe({
        error: () => {
          this.loaded = false;
          this.templates$.next({ discord: {}, status: 'ok', telegram: {} });
        },
        next: t => this.templates$.next(t),
      });
    }
    return this.templates$;
  }
}
