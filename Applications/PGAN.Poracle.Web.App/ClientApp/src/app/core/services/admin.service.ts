import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { AdminUser, Human } from '../models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getUsers(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>(`${this.config.apiHost}/api/admin/users`);
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

  pauseUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/pause`, {});
  }

  resumeUser(userId: string): Observable<Human> {
    return this.http.put<Human>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/resume`, {});
  }

  deleteUserAlarms(userId: string): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/alarms`);
  }

  fetchAvatars(userIds: string[]): Observable<Record<string, string>> {
    return this.http.post<Record<string, string>>(`${this.config.apiHost}/api/admin/users/avatars`, userIds);
  }

  impersonateUser(userId: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}/impersonate`, {});
  }

  deleteUser(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/admin/users/${encodeURIComponent(userId)}`);
  }

  impersonateById(userId: string): Observable<{ token: string }> {
    return this.http.post<{ token: string }>(`${this.config.apiHost}/api/admin/impersonate`, { userId });
  }

  createWebhook(name: string, url: string): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/admin/webhooks`, { name, url });
  }

  getPorocleDelegates(): Observable<Record<string, string[]>> {
    return this.http.get<Record<string, string[]>>(`${this.config.apiHost}/api/admin/poracle-delegates`);
  }

  getPoracleAdmins(): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/admin/poracle-admins`);
  }

  getAllWebhookDelegates(): Observable<Record<string, string[]>> {
    return this.http.get<Record<string, string[]>>(
      `${this.config.apiHost}/api/admin/webhook-delegates/all`,
    );
  }

  getWebhookDelegates(webhookId: string): Observable<string[]> {
    return this.http.get<string[]>(
      `${this.config.apiHost}/api/admin/webhook-delegates?webhookId=${encodeURIComponent(webhookId)}`,
    );
  }

  addWebhookDelegate(webhookId: string, userId: string): Observable<string[]> {
    return this.http.post<string[]>(`${this.config.apiHost}/api/admin/webhook-delegates`, {
      webhookId,
      userId,
    });
  }

  removeWebhookDelegate(webhookId: string, userId: string): Observable<string[]> {
    return this.http.delete<string[]>(`${this.config.apiHost}/api/admin/webhook-delegates`, {
      body: { webhookId, userId },
    });
  }
}
