import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { ConfigService } from './config.service';
import { ActiveHourEntry, parseActiveHours, Profile, ProfileCreate } from '../models';

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(profile: ProfileCreate): Observable<Profile> {
    return this.http.post<Profile>(`${this.config.apiHost}/api/profiles`, profile);
  }

  delete(profileNo: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/profiles/${profileNo}`);
  }

  duplicate(fromProfileNo: number, name: string): Observable<Profile> {
    return this.http.post<Profile>(`${this.config.apiHost}/api/profiles/duplicate`, {
      name,
      fromProfileNo,
    });
  }

  getAll(): Observable<Profile[]> {
    return this.http.get<Profile[]>(`${this.config.apiHost}/api/profiles`).pipe(
      map(profiles =>
        profiles.map(p => ({
          ...p,
          activeHours: parseActiveHours(p.activeHours as unknown as string),
        })),
      ),
    );
  }

  switchProfile(profileNo: number): Observable<{ profile: Profile; token: string }> {
    return this.http.put<{ profile: Profile; token: string }>(`${this.config.apiHost}/api/profiles/switch/${profileNo}`, {});
  }

  update(profileNo: number, name: string): Observable<Profile> {
    return this.http.put<Profile>(`${this.config.apiHost}/api/profiles/${profileNo}`, { name });
  }

  updateActiveHours(profileNo: number, activeHours: ActiveHourEntry[] | null): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/profiles/${profileNo}`, {
      activeHours: activeHours ? JSON.stringify(activeHours) : null,
    });
  }
}
