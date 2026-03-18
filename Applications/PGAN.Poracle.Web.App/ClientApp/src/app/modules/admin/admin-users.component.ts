import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatSelectModule } from '@angular/material/select';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { AdminUser } from '../../core/models';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DiscordAvatarComponent } from '../../shared/components/discord-avatar/discord-avatar.component';

type StatusFilter = 'all' | 'active' | 'stopped' | 'blocked';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatDialogModule,
    MatTooltipModule,
    MatPaginatorModule,
    MatSortModule,
    MatSelectModule,
    DiscordAvatarComponent,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>User Management</h1>
        <p class="page-description">Manage registered Discord users. <strong>Stopped</strong> = user paused alerts or hit rate limits (self-recoverable via !start). <strong>Blocked</strong> = hard-blocked by admin or Poracle auto rate-limit enforcement (requires admin to unblock).</p>
      </div>
    </div>

    <div class="page-content">
      <div class="toolbar">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>Search users</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input
            matInput
            [ngModel]="searchTerm()"
            (ngModelChange)="onSearchChange($event)"
            placeholder="Filter by name or ID..."
          />
          @if (searchTerm()) {
            <button matSuffix mat-icon-button (click)="onSearchChange('')">
              <mat-icon>clear</mat-icon>
            </button>
          }
        </mat-form-field>
        <mat-form-field appearance="outline" class="status-filter">
          <mat-label>Status</mat-label>
          <mat-select [ngModel]="statusFilter()" (ngModelChange)="onStatusFilterChange($event)">
            <mat-option value="all">All</mat-option>
            <mat-option value="active">Active</mat-option>
            <mat-option value="stopped">Stopped</mat-option>
            <mat-option value="blocked">Blocked</mat-option>
          </mat-select>
        </mat-form-field>
        <button mat-raised-button color="primary" (click)="loadUsers()">
          <mat-icon>refresh</mat-icon> Refresh
        </button>
      </div>

      @if (usersLoading()) {
        <div class="loading-container">
          <mat-spinner diameter="48"></mat-spinner>
        </div>
      } @else {
        <div class="table-container">
          <table mat-table matSort (matSortChange)="onSortChange($event)" [dataSource]="paginatedUsers()" class="users-table">
            <!-- ID Column -->
            <ng-container matColumnDef="id">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>ID</th>
              <td mat-cell *matCellDef="let user">{{ user.id }}</td>
            </ng-container>

            <!-- Name Column -->
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
              <td mat-cell *matCellDef="let user">
                <div class="user-name-cell">
                  <app-discord-avatar
                    [userId]="user.id"
                    [defaultUrl]="user.avatarUrl || 'https://cdn.discordapp.com/embed/avatars/0.png'"
                    [userType]="user.type || ''">
                  </app-discord-avatar>
                  <span>{{ user.name || '(unnamed)' }}</span>
                </div>
              </td>
            </ng-container>

            <!-- Status Column -->
            <ng-container matColumnDef="enabled">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
              <td mat-cell *matCellDef="let user">
                @if (user.adminDisable === 1) {
                  <span class="status-chip status-disabled">Blocked</span>
                } @else if (user.enabled === 0) {
                  <span class="status-chip status-paused">Stopped</span>
                } @else {
                  <span class="status-chip status-active">Active</span>
                }
              </td>
            </ng-container>

            <!-- Profile Column -->
            <ng-container matColumnDef="profileNo">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Profile</th>
              <td mat-cell *matCellDef="let user">{{ user.currentProfileNo || 1 }}</td>
            </ng-container>

            <!-- Disabled Since Column -->
            <ng-container matColumnDef="disabledSince">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Disabled Since</th>
              <td mat-cell *matCellDef="let user">
                @if (user.disabledDate) {
                  <span [matTooltip]="formatDate(user.disabledDate)">{{ getRelativeDate(user.disabledDate) }}</span>
                } @else {
                  <span class="text-muted">—</span>
                }
              </td>
            </ng-container>

            <!-- Actions Column -->
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let user">
                <div class="action-buttons">
                  @if (user.adminDisable !== 1) {
                    <button
                      mat-icon-button
                      color="warn"
                      (click)="toggleUser(user, false)"
                      matTooltip="Block user"
                    >
                      <mat-icon>block</mat-icon>
                    </button>
                  } @else {
                    <button
                      mat-icon-button
                      color="primary"
                      (click)="toggleUser(user, true)"
                      matTooltip="Unblock user"
                    >
                      <mat-icon>check_circle</mat-icon>
                    </button>
                  }
                  @if (user.enabled === 0) {
                    <button
                      mat-icon-button
                      color="primary"
                      (click)="resumeUser(user)"
                      matTooltip="Start alerts"
                    >
                      <mat-icon>play_arrow</mat-icon>
                    </button>
                  } @else {
                    <button
                      mat-icon-button
                      (click)="pauseUser(user)"
                      matTooltip="Stop alerts"
                    >
                      <mat-icon>pause</mat-icon>
                    </button>
                  }
                  <button
                    mat-icon-button
                    (click)="impersonate(user)"
                    matTooltip="View as this user"
                  >
                    <mat-icon>visibility</mat-icon>
                  </button>
                  <button
                    mat-icon-button
                    color="warn"
                    (click)="deleteAlarms(user)"
                    matTooltip="Delete all alarms"
                  >
                    <mat-icon>delete_sweep</mat-icon>
                  </button>
                  <button
                    mat-icon-button
                    color="warn"
                    (click)="deleteUser(user)"
                    matTooltip="Delete user account"
                  >
                    <mat-icon>person_remove</mat-icon>
                  </button>
                </div>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="userColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: userColumns;"></tr>
          </table>
        </div>

        @if (filteredUsers().length === 0) {
          <div class="empty-state">
            <mat-icon class="empty-icon">people_outline</mat-icon>
            <h2>No users found</h2>
            <p>{{ searchTerm() || statusFilter() !== 'all' ? 'Try adjusting your filters.' : 'No users registered yet.' }}</p>
          </div>
        }

        @if (filteredUsers().length > 0) {
          <mat-paginator
            [length]="filteredUsers().length"
            [pageSize]="pageSize()"
            [pageIndex]="pageIndex()"
            [pageSizeOptions]="[10, 25, 50, 100]"
            (page)="onPageChange($event)"
            showFirstLastButtons
          ></mat-paginator>
        }
      }
    </div>
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 20px 24px;
        gap: 16px;
        background: linear-gradient(135deg, rgba(69,90,100,0.06) 0%, rgba(0,137,123,0.04) 100%);
        border-radius: 12px;
        margin-bottom: 16px;
      }
      .page-header-text {
        flex: 1;
        min-width: 0;
      }
      .page-header h1 {
        margin: 0;
        font-size: 24px;
        font-weight: 400;
      }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .page-content {
        padding: 0 24px 24px;
      }
      .toolbar {
        display: flex;
        align-items: center;
        gap: 16px;
        margin-bottom: 16px;
        flex-wrap: wrap;
      }
      .search-field {
        flex: 1;
        max-width: 480px;
        min-width: 200px;
      }
      .status-filter {
        width: 180px;
      }
      .loading-container {
        display: flex;
        justify-content: center;
        padding: 64px;
      }
      .table-container {
        overflow-x: auto;
      }
      .users-table {
        width: 100%;
      }
      .users-table tr.mat-mdc-row:hover {
        background: rgba(0, 0, 0, 0.04);
      }
      .user-name-cell {
        display: flex;
        align-items: center;
        gap: 10px;
      }
      .status-chip {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 12px;
        font-weight: 500;
      }
      .status-active {
        background: #4caf50;
        color: white;
      }
      .status-paused {
        background: #ff9800;
        color: white;
      }
      .status-disabled {
        background: #f44336;
        color: white;
      }
      .action-buttons {
        display: flex;
        gap: 4px;
      }
      .empty-state {
        text-align: center;
        padding: 64px 16px;
      }
      .empty-icon {
        font-size: 64px;
        width: 64px;
        height: 64px;
        color: var(--text-hint, rgba(0, 0, 0, 0.24));
      }
      .empty-state h2 {
        margin: 16px 0 8px;
        font-weight: 400;
      }
      .empty-state p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .text-muted {
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
      }
    `,
  ],
})
export class AdminUsersComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  private readonly allUsers = signal<AdminUser[]>([]);
  readonly usersLoading = signal(true);

  readonly searchTerm = signal('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly pageIndex = signal(parseInt(sessionStorage.getItem('admin_pageIndex') || '0', 10));
  readonly pageSize = signal(parseInt(sessionStorage.getItem('admin_pageSize') || '25', 10));
  readonly userColumns = ['name', 'id', 'enabled', 'disabledSince', 'profileNo', 'actions'];
  readonly sortActive = signal('');
  readonly sortDirection = signal<'asc' | 'desc' | ''>('');

  private readonly discordUsers = computed(() =>
    this.allUsers().filter((u) => u.type?.startsWith('discord')),
  );

  readonly filteredUsers = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const status = this.statusFilter();
    let users = this.discordUsers();

    if (term) {
      users = users.filter(
        (u) => u.id.toLowerCase().includes(term) || (u.name || '').toLowerCase().includes(term),
      );
    }

    if (status !== 'all') {
      users = users.filter((u) => {
        if (status === 'blocked') return u.adminDisable === 1;
        if (status === 'stopped') return u.adminDisable !== 1 && u.enabled === 0;
        if (status === 'active') return u.adminDisable !== 1 && u.enabled !== 0;
        return true;
      });
    }

    const active = this.sortActive();
    const direction = this.sortDirection();
    if (!active || !direction) return users;

    return [...users].sort((a, b) => {
      const dir = direction === 'asc' ? 1 : -1;
      const valA = this.getSortValue(a, active);
      const valB = this.getSortValue(b, active);
      if (valA < valB) return -1 * dir;
      if (valA > valB) return 1 * dir;
      return 0;
    });
  });

  readonly paginatedUsers = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.filteredUsers().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    this.adminService.getUsers().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (users) => {
        this.allUsers.set(users);
        this.usersLoading.set(false);
      },
      error: () => {
        this.usersLoading.set(false);
        this.snackBar.open('Failed to load users', 'OK', { duration: 3000 });
      },
    });
  }

  onSearchChange(term: string): void {
    this.searchTerm.set(term);
    this.pageIndex.set(0);
  }

  onStatusFilterChange(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.pageIndex.set(0);
  }

  onSortChange(sort: Sort): void {
    this.sortActive.set(sort.active);
    this.sortDirection.set(sort.direction);
    this.pageIndex.set(0);
  }

  private getSortValue(user: AdminUser, column: string): string | number {
    switch (column) {
      case 'id': return user.id;
      case 'name': return (user.name || '').toLowerCase();
      case 'enabled': return user.adminDisable === 1 ? 2 : user.enabled === 0 ? 1 : 0;
      case 'profileNo': return user.currentProfileNo || 1;
      case 'disabledSince': return user.disabledDate ? new Date(user.disabledDate).getTime() : 0;
      default: return '';
    }
  }

  getRelativeDate(dateStr: string): string {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return '—';
    const days = Math.floor((Date.now() - d.getTime()) / 86400000);
    if (days === 0) return 'Today';
    if (days === 1) return 'Yesterday';
    if (days < 30) return `${days}d ago`;
    if (days < 365) return `${Math.floor(days / 30)}mo ago`;
    return `${Math.floor(days / 365)}y ago`;
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? '' : d.toLocaleString();
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    sessionStorage.setItem('admin_pageIndex', String(event.pageIndex));
    sessionStorage.setItem('admin_pageSize', String(event.pageSize));
  }

  toggleUser(user: AdminUser, enable: boolean): void {
    const action$ = enable
      ? this.adminService.enableUser(user.id)
      : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.allUsers.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, adminDisable: updated.adminDisable } : u)),
        );
        this.snackBar.open(
          `"${user.name || user.id}" ${enable ? 'unblocked' : 'blocked'}`,
          'OK',
          { duration: 3000 },
        );
      },
      error: () => {
        this.snackBar.open(`Failed to ${enable ? 'unblock' : 'block'}`, 'OK', {
          duration: 3000,
        });
      },
    });
  }

  pauseUser(user: AdminUser): void {
    this.adminService.pauseUser(user.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.allUsers.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)),
        );
        this.snackBar.open(`Alerts stopped for "${user.name || user.id}"`, 'OK', { duration: 3000 });
      },
      error: () => {
        this.snackBar.open('Failed to stop alerts', 'OK', { duration: 3000 });
      },
    });
  }

  resumeUser(user: AdminUser): void {
    this.adminService.resumeUser(user.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.allUsers.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)),
        );
        this.snackBar.open(`Alerts resumed for "${user.name || user.id}"`, 'OK', { duration: 3000 });
      },
      error: () => {
        this.snackBar.open('Failed to resume alerts', 'OK', { duration: 3000 });
      },
    });
  }

  deleteAlarms(user: AdminUser): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete All Alarms',
        message: `Are you sure you want to delete ALL alarms for "${user.name || user.id}"? This action cannot be undone.`,
        confirmText: 'Delete All',
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.adminService.deleteUserAlarms(user.id).subscribe({
          next: (result) => {
            this.snackBar.open(
              `Deleted ${result.deleted} alarm(s) for "${user.name || user.id}"`,
              'OK',
              { duration: 3000 },
            );
          },
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }

  impersonate(user: AdminUser): void {
    this.adminService.impersonateUser(user.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res) => {
        this.auth.impersonate(res.token);
      },
      error: () => {
        this.snackBar.open('Failed to impersonate user', 'OK', { duration: 3000 });
      },
    });
  }

  deleteUser(user: AdminUser): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete User',
        message: `Are you sure you want to permanently delete "${user.name || user.id}"? This will remove their account but NOT their alarms. Use "Delete all alarms" first if needed.`,
        confirmText: 'Delete User',
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.adminService.deleteUser(user.id).subscribe({
          next: () => {
            this.allUsers.update((users) => users.filter((u) => u.id !== user.id));
            this.snackBar.open(`"${user.name || user.id}" deleted`, 'OK', { duration: 3000 });
          },
          error: () => {
            this.snackBar.open('Failed to delete user', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }
}
