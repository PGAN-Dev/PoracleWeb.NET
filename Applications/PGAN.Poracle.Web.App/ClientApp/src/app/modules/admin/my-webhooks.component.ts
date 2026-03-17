import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { AdminUser } from '../../core/models';

@Component({
  selector: 'app-my-webhooks',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>My Webhooks</h1>
        <p class="page-description">Webhooks you are delegated to manage. Click "Manage Alarms" to configure alerts for a webhook.</p>
      </div>
    </div>

    <div class="page-content">
      @if (loading()) {
        <div class="loading-container">
          <mat-spinner diameter="48"></mat-spinner>
        </div>
      } @else if (webhooks().length === 0) {
        <div class="empty-state">
          <mat-icon class="empty-icon">webhook</mat-icon>
          <h2>No webhooks assigned</h2>
          <p>You have not been assigned as a delegate for any webhooks.</p>
        </div>
      } @else {
        <div class="table-container">
          <table mat-table [dataSource]="webhooks()" class="webhooks-table">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef>Name</th>
              <td mat-cell *matCellDef="let wh">{{ wh.name || '(unnamed)' }}</td>
            </ng-container>

            <ng-container matColumnDef="url">
              <th mat-header-cell *matHeaderCellDef>URL</th>
              <td mat-cell *matCellDef="let wh">
                <span class="webhook-url" [matTooltip]="wh.id" matTooltipClass="url-tooltip">
                  {{ truncateUrl(wh.id) }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Status</th>
              <td mat-cell *matCellDef="let wh">
                @if (wh.adminDisable === 1) {
                  <span class="status-chip status-disabled">Blocked</span>
                } @else if (wh.enabled === 0) {
                  <span class="status-chip status-paused">Stopped</span>
                } @else {
                  <span class="status-chip status-active">Active</span>
                }
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let wh">
                <button
                  mat-raised-button
                  color="primary"
                  (click)="manageAlarms(wh)"
                  matTooltip="Manage alarms for this webhook"
                >
                  <mat-icon>tune</mat-icon> Manage Alarms
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns;"></tr>
          </table>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 16px 24px;
        gap: 16px;
      }
      .page-header-text { flex: 1; min-width: 0; }
      .page-header h1 { margin: 0; font-size: 24px; font-weight: 400; }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .page-content { padding: 0 24px 24px; }
      .loading-container { display: flex; justify-content: center; padding: 64px; }
      .table-container { overflow-x: auto; }
      .webhooks-table { width: 100%; }
      .webhook-url {
        font-family: monospace;
        font-size: 12px;
        color: var(--text-secondary, rgba(0,0,0,0.6));
      }
      .status-chip {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 12px;
        font-weight: 500;
      }
      .status-active { background: #4caf50; color: white; }
      .status-paused { background: #ff9800; color: white; }
      .status-disabled { background: #f44336; color: white; }
      .empty-state { text-align: center; padding: 64px 16px; }
      .empty-icon {
        font-size: 64px; width: 64px; height: 64px;
        color: var(--text-hint, rgba(0, 0, 0, 0.24));
      }
      .empty-state h2 { margin: 16px 0 8px; font-weight: 400; }
      .empty-state p { color: var(--text-secondary, rgba(0, 0, 0, 0.54)); }
    `,
  ],
})
export class MyWebhooksComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly webhooks = signal<AdminUser[]>([]);
  readonly columns = ['name', 'url', 'status', 'actions'];

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
        next: (users) => {
          this.webhooks.set(users.filter((u) => managedIds.includes(u.id)));
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load webhooks', 'OK', { duration: 3000 });
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

  manageAlarms(webhook: AdminUser): void {
    this.adminService
      .impersonateById(webhook.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => this.auth.impersonate(res.token),
        error: () => this.snackBar.open('Failed to switch to webhook context', 'OK', { duration: 3000 }),
      });
  }
}
