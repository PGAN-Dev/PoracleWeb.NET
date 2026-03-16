import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Human } from '../models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getUsers(): Observable<any[]> {
    return this.http.get<any[]>(`${this.config.apiHost}/api/admin/users`);
  }

  getUser(userId: string): Observable<Human> {
    return this.http.get<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}`);
  }

  enableUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/enable`, {});
  }

  disableUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/disable`, {});
  }

  deleteUserAlarms(userId: string): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/alarms`);
  }

  fetchAvatars(userIds: string[]): Observable<Record<string, string>> {
    return this.http.post<Record<string, string>>(`${this.config.apiHost}/api/admin/users/avatars`, userIds);
  }
}
