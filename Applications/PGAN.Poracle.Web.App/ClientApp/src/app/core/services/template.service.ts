import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, ReplaySubject, map } from 'rxjs';
import { ConfigService } from './config.service';
import { AuthService } from './auth.service';

export interface TemplateData {
  status: string;
  discord: Record<string, Record<string, (string | number)[]>>;
  telegram: Record<string, Record<string, (string | number)[]>>;
}

@Injectable({ providedIn: 'root' })
export class TemplateService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);

  private templates$ = new ReplaySubject<TemplateData>(1);
  private loaded = false;

  loadTemplates(): Observable<TemplateData> {
    if (!this.loaded) {
      this.loaded = true;
      this.http.get<TemplateData>(`${this.config.apiHost}/api/config/templates`)
        .subscribe({
          next: t => this.templates$.next(t),
          error: () => {
            this.loaded = false;
            this.templates$.next({ status: 'ok', discord: {}, telegram: {} });
          },
        });
    }
    return this.templates$;
  }

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
      })
    );
  }
}
