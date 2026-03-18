import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Profile, ProfileCreate } from '../models';

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  copy(fromProfile: number, toProfile: number): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/profiles/copy`, {
      fromProfile,
      toProfile,
    });
  }

  create(profile: ProfileCreate): Observable<Profile> {
    return this.http.post<Profile>(`${this.config.apiHost}/api/profiles`, profile);
  }

  delete(profileNo: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/profiles/${profileNo}`);
  }

  getAll(): Observable<Profile[]> {
    return this.http.get<Profile[]>(`${this.config.apiHost}/api/profiles`);
  }

  switchProfile(profileNo: number): Observable<Profile> {
    return this.http.put<Profile>(`${this.config.apiHost}/api/profiles/switch/${profileNo}`, {});
  }

  update(profileNo: number, name: string): Observable<Profile> {
    return this.http.put<Profile>(`${this.config.apiHost}/api/profiles/${profileNo}`, { name });
  }
}
