import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { DashboardCounts } from '../models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  getCounts(): Observable<DashboardCounts> {
    return this.http.get<DashboardCounts>(`${this.config.apiHost}/api/dashboard`);
  }
}
