import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PoracleServerStatus } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';
import { I18nService } from '../../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslateModule,
  ],
  selector: 'app-poracle-servers',
  standalone: true,
  styleUrl: './poracle-servers.component.scss',
  templateUrl: './poracle-servers.component.html',
})
export class PoracleServersComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly restarting = signal<Record<string, boolean>>({});
  readonly restartingAll = signal(false);
  readonly servers = signal<PoracleServerStatus[]>([]);

  ngOnInit(): void {
    this.loadServers();
  }

  refreshServers(): void {
    this.loadServers();
  }

  async restartAll(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.RESTART_ALL'),
        message: this.i18n.instant('ADMIN.CONFIRM_RESTART_ALL'),
        title: this.i18n.instant('ADMIN.RESTART_ALL_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.restartingAll.set(true);
      this.adminService
        .restartAllServers()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => {
            this.restartingAll.set(false);
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_RESTART_ALL'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: servers => {
            this.servers.set(servers);
            this.restartingAll.set(false);
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_ALL_RESTARTED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
        });
    }
  }

  async restartServer(server: PoracleServerStatus): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.RESTART'),
        message: this.i18n.instant('ADMIN.CONFIRM_RESTART_SERVER', { name: server.name, host: server.host }),
        title: this.i18n.instant('ADMIN.RESTART_SERVER_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.restarting.update(r => ({ ...r, [server.host]: true }));
      this.adminService
        .restartServer(server.host)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => {
            this.restarting.update(r => ({ ...r, [server.host]: false }));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_RESTART', { name: server.name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
          next: updated => {
            this.servers.update(list => list.map(s => (s.host === updated.host ? updated : s)));
            this.restarting.update(r => ({ ...r, [server.host]: false }));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_RESTARTED', { name: server.name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
        });
    }
  }

  private loadServers(): void {
    this.loading.set(true);
    this.adminService
      .getPoracleServers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_SERVERS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: servers => {
          this.servers.set(servers);
          this.loading.set(false);
        },
      });
  }
}
