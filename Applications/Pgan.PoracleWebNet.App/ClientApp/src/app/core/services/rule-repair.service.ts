import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, throwError } from 'rxjs';

import { ConfigService } from './config.service';
import { RuleRepairStatus } from '../models';

/**
 * The admin rule repair job. A 409 from scan or apply means a run is already going (or, for apply,
 * nothing has been scanned); the body is the current status, so it is returned as an answer rather than
 * thrown, and the card shows the state it describes. The full status carries every finding, which can run
 * to thousands of rows, so a progress poll asks for the summary instead.
 */
@Injectable({ providedIn: 'root' })
export class RuleRepairService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  apply(): Observable<RuleRepairStatus> {
    return this.post('apply');
  }

  /** With `summary`, the three lists come back empty and only their counts are filled in. */
  getStatus(summary = false): Observable<RuleRepairStatus> {
    return this.http.get<RuleRepairStatus>(this.url(''), summary ? { params: { summary: 'true' } } : {});
  }

  scan(): Observable<RuleRepairStatus> {
    return this.post('scan');
  }

  private post(action: 'apply' | 'scan'): Observable<RuleRepairStatus> {
    return this.http
      .post<RuleRepairStatus>(this.url(`/${action}`), {})
      .pipe(
        catchError((err: unknown) =>
          err instanceof HttpErrorResponse && err.status === 409 && err.error?.phase
            ? of(err.error as RuleRepairStatus)
            : throwError(() => err),
        ),
      );
  }

  private url(suffix: string): string {
    return `${this.config.apiHost}/api/admin/rule-repair${suffix}`;
  }
}
