import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { DashboardCounts } from '../models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getCounts(): Observable<DashboardCounts> {
    return this.http.get<DashboardCounts>(`${this.config.apiHost}/api/dashboard`);
  }
}
