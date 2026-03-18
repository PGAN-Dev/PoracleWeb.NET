import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { AdminUser, Human } from '../models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  addWebhookDelegate(webhookId: string, userId: string): Observable<string[]> {
    return this.http.post<string[]>(`${this.config.apiHost}/api/admin/webhook-delegates`, {
      userId,
      webhookId,
    });
  }

  createWebhook(name: string, url: string): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/admin/webhooks`, { name, url });
  }

  deleteUser(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}`);
  }

  deleteUserAlarms(userId: string): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/alarms`);
  }

  disableUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/disable`, {});
  }

  enableUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/enable`, {});
  }

  fetchAvatars(userIds: string[]): Observable<Record<string, string>> {
    return this.http.post<Record<string, string>>(`${this.config.apiHost}/api/admin/users/avatars`, userIds);
  }

  getAllWebhookDelegates(): Observable<Record<string, string[]>> {
    return this.http.get<Record<string, string[]>>(`${this.config.apiHost}/api/admin/webhook-delegates/all`);
  }

  getPoracleAdmins(): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/admin/poracle-admins`);
  }

  getPorocleDelegates(): Observable<Record<string, string[]>> {
    return this.http.get<Record<string, string[]>>(`${this.config.apiHost}/api/admin/poracle-delegates`);
  }

  getUser(userId: string): Observable<Human> {
    return this.http.get<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}`);
  }

  getUsers(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>(`${this.config.apiHost}/api/admin/users`);
  }

  getWebhookDelegates(webhookId: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/admin/webhook-delegates?webhookId=${encodeURIComponent(webhookId)}`);
  }

  impersonateById(userId: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.config.apiHost}/api/admin/impersonate`, { userId });
  }

  impersonateUser(userId: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/impersonate`, {});
  }

  pauseUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/pause`, {});
  }

  removeWebhookDelegate(webhookId: string, userId: string): Observable<string[]> {
    return this.http.delete<string[]>(`${this.config.apiHost}/api/admin/webhook-delegates`, {
      body: { userId, webhookId },
    });
  }

  resumeUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/resume`, {});
  }
}
