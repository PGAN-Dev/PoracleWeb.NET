import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';

import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';

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
    TranslateModule,
  ],
  selector: 'app-my-webhooks',
  standalone: true,
  styleUrl: './my-webhooks.component.scss',
  templateUrl: './my-webhooks.component.html',
})
export class MyWebhooksComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = ['name', 'url', 'status', 'actions'];
  readonly loading = signal(true);
  readonly webhooks = signal<AdminUser[]>([]);

  manageAlarms(webhook: AdminUser): void {
    this.adminService
      .impersonateById(webhook.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () =>
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
        next: res => this.auth.impersonate(res.token),
      });
  }

  ngOnInit(): void {
    const managedIds = this.auth.managedWebhooks();
    if (managedIds.length === 0) {
      this.loading.set(false);
      return;
    }

    this.adminService
      .getUsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_WEBHOOKS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: users => {
          this.webhooks.set(users.filter(u => managedIds.includes(u.id)));
          this.loading.set(false);
        },
      });
  }

  truncateUrl(url: string): string {
    try {
      const u = new URL(url);
      const path = u.pathname.length > 24 ? '…' + u.pathname.slice(-20) : u.pathname;
      return `${u.hostname}${path}`;
    } catch {
      return url.length > 40 ? url.slice(0, 37) + '…' : url;
    }
  }
}
