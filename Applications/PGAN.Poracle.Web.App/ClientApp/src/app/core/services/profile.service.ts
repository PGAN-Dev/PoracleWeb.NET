import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Profile, ProfileCreate } from '../models';

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Profile[]> {
    return this.http.get<Profile[]>(`${this.config.apiHost}/api/profiles`);
  }

  create(profile: ProfileCreate): Observable<Profile> {
    return this.http.post<Profile>(`${this.config.apiHost}/api/profiles`, profile);
  }

  update(profileNo: number, name: string): Observable<Profile> {
    return this.http.put<Profile>(
      `${this.config.apiHost}/api/profiles/${profileNo}`,
      { name },
    );
  }

  switchProfile(profileNo: number): Observable<Profile> {
    return this.http.put<Profile>(
      `${this.config.apiHost}/api/profiles/switch/${profileNo}`,
      {},
    );
  }

  delete(profileNo: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/profiles/${profileNo}`);
  }

  copy(fromProfile: number, toProfile: number): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/profiles/copy`, {
      fromProfile,
      toProfile,
    });
  }
}
