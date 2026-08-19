import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { PoracleServerProfile } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';

/**
 * The admin landing page, which now opens by saying what PoracleNG this deployment is talking to.
 *
 * Nothing in the UI said that before, so a server too old for the features this build ships looked
 * exactly like a bug in PoracleWeb: the control saves, the column does not exist, the filter does
 * nothing, and the only clue is in the logs.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, MatListModule, MatIconModule, MatCardModule, MatButtonModule, MatProgressSpinnerModule, TranslatePipe],
  selector: 'app-admin',
  standalone: true,
  styleUrl: './admin.component.scss',
  templateUrl: './admin.component.html',
})
export class AdminComponent implements OnInit {
  private readonly adminService = inject(AdminService);

  readonly profile = signal<PoracleServerProfile | null>(null);

  /** Only the capabilities that are on; a false key means the binary knows it and has it switched off. */
  readonly enabledCapabilities = computed(() =>
    Object.entries(this.profile()?.capabilities ?? {})
      .filter(([, enabled]) => enabled)
      .map(([name]) => name)
      .sort((a, b) => a.localeCompare(b)),
  );

  readonly loading = signal(true);

  ngOnInit(): void {
    this.load(false);
  }

  refresh(): void {
    this.load(true);
  }

  private load(refresh: boolean): void {
    this.loading.set(true);
    this.adminService.getServerProfile(refresh).subscribe({
      // A failed probe is itself an answer, and the card says so rather than staying blank forever.
      error: () => {
        this.profile.set(null);
        this.loading.set(false);
      },
      next: profile => {
        this.profile.set(profile);
        this.loading.set(false);
      },
    });
  }
}
