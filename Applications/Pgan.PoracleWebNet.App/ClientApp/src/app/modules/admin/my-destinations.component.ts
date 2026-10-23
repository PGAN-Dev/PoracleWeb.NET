import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { isKnownKind, kindIcon, kindLabelKey } from '../../shared/utils/human-kind';
import { serverReason } from '../../shared/utils/server-reason';
import { shortWebhookUrl } from '../../shared/utils/webhook-url';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
    NgTemplateOutlet,
    TranslatePipe,
  ],
  selector: 'app-my-destinations',
  standalone: true,
  styleUrl: './my-destinations.component.scss',
  templateUrl: './my-destinations.component.html',
})
export class MyDestinationsComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = ['name', 'type', 'url', 'status', 'actions'];
  /** One slot holds the pre-impersonation token, so a second hop would strand the caller. See #797. */
  readonly destinations = signal<AdminUser[]>([]);
  readonly isImpersonating = this.auth.isImpersonating;
  readonly isKnownKind = isKnownKind;
  readonly kindIcon = kindIcon;
  readonly kindLabelKey = kindLabelKey;
  readonly loading = signal(true);

  constructor() {
    // The list loaded inside a managed session is the managed destination's, which has none. When the
    // session ends, from the note here or the banner, the caller's own list has to be fetched again.
    let wasManaging = untracked(this.isImpersonating);
    effect(() => {
      const managing = this.isImpersonating();
      if (wasManaging && !managing) untracked(() => this.load());
      wasManaging = managing;
    });
  }

  exitManaging(): void {
    void this.auth.stopImpersonating();
  }

  /** A webhook id is a URL with a secret token in it, so only its host and hook number are shown. */
  idLabel(row: AdminUser): string {
    return this.isWebhook(row) ? shortWebhookUrl(row.id) : row.id;
  }

  isWebhook(row: AdminUser): boolean {
    return (row.type ?? '').trim().toLowerCase() === 'webhook';
  }

  manageAlarms(destination: AdminUser): void {
    this.adminService
      .impersonateById(destination.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err =>
          this.snackBar.open(
            serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_SWITCH_DESTINATION'),
            this.i18n.instant('TOAST.OK'),
            {
              duration: 4000,
            },
          ),
        next: res => this.auth.impersonate(res.token, '/my-destinations'),
      });
  }

  /**
   * The rows come back scoped to the caller, so they are rendered as they arrive.
   *
   * They used to be intersected with `auth.managedDestinations()` first, which turned any disagreement
   * between the two lists into an empty table with no error -- the shape of #797, where the server
   * resolved a name-keyed grant to a webhook the client's copy of the list still named by name.
   * The second filter could only ever subtract from an already-authorised set.
   */
  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.adminService
      .getManagedDestinations()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_DESTINATIONS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: users => {
          this.destinations.set(users);
          this.loading.set(false);
        },
      });
  }
}
