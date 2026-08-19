import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';

import { PoracleServerProfile } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';

/**
 * Says which PoracleNG this deployment talks to, and warns when it is too old for this build.
 *
 * Nothing in the UI said this before. A server below the minimum fails the same quiet way for every
 * feature that needs it — the control saves, the column does not exist, the filter does nothing — and
 * the only clue was in the logs.
 *
 * Its own component rather than more markup inside the settings page: that page is already long, and
 * this is testable on its own, which the first attempt at putting it on the unrouted admin landing page
 * was not.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, TranslatePipe],
  selector: 'app-server-profile-card',
  standalone: true,
  styleUrl: './server-profile-card.component.scss',
  templateUrl: './server-profile-card.component.html',
})
export class ServerProfileCardComponent implements OnInit {
  private readonly adminService = inject(AdminService);

  readonly serverProfile = signal<PoracleServerProfile | null>(null);

  /** Only the capabilities that are on; a false key means the binary knows it and has it switched off. */
  readonly enabledCapabilities = computed(() =>
    Object.entries(this.serverProfile()?.capabilities ?? {})
      .filter(([, enabled]) => enabled)
      .map(([name]) => name)
      .sort((a, b) => a.localeCompare(b)),
  );

  readonly serverLoading = signal(true);

  /**
   * The two components this deployment is made of, so the card says "you are behind" once per project
   * rather than making an admin compare version strings by eye.
   */
  readonly updates = computed(() => {
    const profile = this.serverProfile();
    if (!profile) return [];

    return [
      { label: 'PoracleWeb', status: profile.webUpdate },
      { label: 'Poracle', status: profile.poracleUpdate },
    ].filter(u => u.status);
  });

  ngOnInit(): void {
    this.load(false);
  }

  refreshServerProfile(): void {
    this.load(true);
  }

  private load(refresh: boolean): void {
    this.serverLoading.set(true);
    this.adminService.getServerProfile(refresh).subscribe({
      // A failed probe is itself an answer; the card says so rather than spinning forever.
      error: () => {
        this.serverProfile.set(null);
        this.serverLoading.set(false);
      },
      next: profile => {
        this.serverProfile.set(profile);
        this.serverLoading.set(false);
      },
    });
  }
}
