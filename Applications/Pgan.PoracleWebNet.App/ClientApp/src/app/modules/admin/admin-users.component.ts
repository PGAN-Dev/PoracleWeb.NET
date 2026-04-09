import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';

import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DiscordAvatarComponent } from '../../shared/components/discord-avatar/discord-avatar.component';

type StatusFilter = 'all' | 'active' | 'stopped' | 'blocked';

@Component({
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
    TranslateModule,
    DiscordAvatarComponent,
  ],
  selector: 'app-admin-users',
  standalone: true,
  styleUrl: './admin-users.component.scss',
  templateUrl: './admin-users.component.html',
})
export class AdminUsersComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly allUsers = signal<AdminUser[]>([]);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly discordUsers = computed(() => this.allUsers().filter(u => u.type?.startsWith('discord')));

  private readonly i18n = inject(I18nService);

  private readonly snackBar = inject(MatSnackBar);

  readonly searchTerm = signal('');
  readonly sortActive = signal('');
  readonly sortDirection = signal<'asc' | 'desc' | ''>('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly filteredUsers = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const status = this.statusFilter();
    let users = this.discordUsers();

    if (term) {
      users = users.filter(u => u.id.toLowerCase().includes(term) || (u.name || '').toLowerCase().includes(term));
    }

    if (status !== 'all') {
      users = users.filter(u => {
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

  readonly pageIndex = signal(parseInt(sessionStorage.getItem('admin_pageIndex') || '0', 10));
  readonly pageSize = signal(parseInt(sessionStorage.getItem('admin_pageSize') || '25', 10));

  readonly paginatedUsers = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.filteredUsers().slice(start, start + this.pageSize());
  });

  readonly userColumns = ['name', 'id', 'enabled', 'disabledSince', 'profileNo', 'actions'];

  readonly usersLoading = signal(true);

  deleteAlarms(user: AdminUser): void {
    const name = user.name || user.id;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('ADMIN.CONFIRM_DELETE_ALARMS', { name }),
        title: this.i18n.instant('ADMIN.DELETE_ALL_ALARMS'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUserAlarms(user.id).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_ALARMS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: result => {
            this.snackBar.open(
              this.i18n.instant('ADMIN.SNACK_ALARMS_DELETED', { name, count: result.deleted }),
              this.i18n.instant('TOAST.OK'),
              { duration: 3000 },
            );
          },
        });
      }
    });
  }

  deleteUser(user: AdminUser): void {
    const name = user.name || user.id;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.DELETE_USER'),
        message: this.i18n.instant('ADMIN.CONFIRM_DELETE_USER', { name }),
        title: this.i18n.instant('ADMIN.DELETE_USER'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUser(user.id).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.allUsers.update(users => users.filter(u => u.id !== user.id));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_DELETED', { name }), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
        });
      }
    });
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? '' : d.toLocaleString();
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

  impersonate(user: AdminUser): void {
    this.adminService
      .impersonateUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_IMPERSONATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: res => {
          this.auth.impersonate(res.token);
        },
      });
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    this.adminService
      .getUsers()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.usersLoading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_USERS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: users => {
          this.allUsers.set(users);
          this.usersLoading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    sessionStorage.setItem('admin_pageIndex', String(event.pageIndex));
    sessionStorage.setItem('admin_pageSize', String(event.pageSize));
  }

  onSearchChange(term: string): void {
    this.searchTerm.set(term);
    this.pageIndex.set(0);
  }

  onSortChange(sort: Sort): void {
    this.sortActive.set(sort.active);
    this.sortDirection.set(sort.direction);
    this.pageIndex.set(0);
  }

  onStatusFilterChange(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.pageIndex.set(0);
  }

  pauseUser(user: AdminUser): void {
    this.adminService
      .pauseUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_STOP'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: updated => {
          this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)));
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_STOPPED', { name: user.name || user.id }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
      });
  }

  resumeUser(user: AdminUser): void {
    this.adminService
      .resumeUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_START'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: updated => {
          this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)));
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_STARTED', { name: user.name || user.id }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
      });
  }

  toggleUser(user: AdminUser, enable: boolean): void {
    const action$ = enable ? this.adminService.enableUser(user.id) : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_BLOCK'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      },
      next: updated => {
        this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, adminDisable: updated.adminDisable } : u)));
        const name = user.name || user.id;
        this.snackBar.open(
          this.i18n.instant(enable ? 'ADMIN.SNACK_UNBLOCKED' : 'ADMIN.SNACK_BLOCKED', { name }),
          this.i18n.instant('TOAST.OK'),
          { duration: 3000 },
        );
      },
    });
  }

  private getSortValue(user: AdminUser, column: string): string | number {
    switch (column) {
      case 'id':
        return user.id;
      case 'name':
        return (user.name || '').toLowerCase();
      case 'enabled':
        return user.adminDisable === 1 ? 2 : user.enabled === 0 ? 1 : 0;
      case 'profileNo':
        return user.currentProfileNo || 1;
      case 'disabledSince':
        return user.disabledDate ? new Date(user.disabledDate).getTime() : 0;
      default:
        return '';
    }
  }
}
