import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { ConfigService } from './config.service';

interface VersionResponse {
  channel?: string;
}

/**
 * Which build the server is running, from the anonymous `GET /api/version`. Only the release channel
 * is read: `experimental` for the unofficial image, `official` for everything else. It stays null until
 * the answer arrives, and stays null for good if the request fails or an older server omits the field,
 * so nothing is ever labelled on a guess.
 */
@Injectable({ providedIn: 'root' })
export class BuildInfoService {
  private readonly channelSignal = signal<null | string>(null);
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private requested = false;

  readonly channel = this.channelSignal.asReadonly();

  /** One GET per page load. Errors are swallowed: the channel is a label, not something to fail over. */
  load(): void {
    if (this.requested) return;
    this.requested = true;
    this.http.get<VersionResponse>(`${this.config.apiHost}/api/version`).subscribe({
      error: () => undefined,
      next: info => this.channelSignal.set(info?.channel || null),
    });
  }
}
