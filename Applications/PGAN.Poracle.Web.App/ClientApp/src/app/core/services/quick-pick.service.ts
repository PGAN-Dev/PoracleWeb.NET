import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { QuickPickAppliedState, QuickPickApplyRequest, QuickPickDefinition, QuickPickSummary } from '../models';
import { ConfigService } from './config.service';

@Injectable({ providedIn: 'root' })
export class QuickPickService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private get baseUrl(): string {
    return `${this.config.apiHost}/api/quick-picks`;
  }

  apply(id: string, request: QuickPickApplyRequest): Observable<QuickPickAppliedState> {
    return this.http.post<QuickPickAppliedState>(`${this.baseUrl}/${id}/apply`, request);
  }

  deleteAdmin(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  deleteUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/user/${id}`);
  }

  getAll(): Observable<QuickPickSummary[]> {
    return this.http.get<QuickPickSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<QuickPickDefinition> {
    return this.http.get<QuickPickDefinition>(`${this.baseUrl}/${id}`);
  }

  reapply(id: string, request: QuickPickApplyRequest): Observable<QuickPickAppliedState> {
    return this.http.post<QuickPickAppliedState>(`${this.baseUrl}/${id}/reapply`, request);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/remove`);
  }

  saveAdmin(definition: QuickPickDefinition): Observable<QuickPickDefinition> {
    return this.http.post<QuickPickDefinition>(this.baseUrl, definition);
  }

  saveUser(definition: QuickPickDefinition): Observable<QuickPickDefinition> {
    return this.http.post<QuickPickDefinition>(`${this.baseUrl}/user`, definition);
  }

  seed(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/seed`, {});
  }
}
