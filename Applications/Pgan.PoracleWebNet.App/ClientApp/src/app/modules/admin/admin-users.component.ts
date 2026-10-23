import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DiscordAvatarComponent } from '../../shared/components/discord-avatar/discord-avatar.component';
import { GameAccountsDialogComponent } from '../../shared/components/game-accounts-dialog/game-accounts-dialog.component';
import { serverReason } from '../../shared/utils/server-reason';

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
    MatDividerModule,
    MatMenuModule,
    MatTooltipModule,
    MatPaginatorModule,
    MatSortModule,
    MatSelectModule,
    TranslatePipe,
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
  // Everything that is not a webhook, so this tab and the Webhooks tab together account for every
  // account. Filtering to discord* meant a Telegram user appeared in neither list, and every admin
  // action here is list-driven -- so they could not be blocked, paused, purged or impersonated at all.
  // See #632.
  private readonly discordUsers = computed(() => this.allUsers().filter(u => u.type !== 'webhook'));

  private readonly i18n = inject(I18nService);

  private readonly poracleConfig = inject(PoracleConfigService);

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
      users = users.filter(
        u =>
          u.id.toLowerCase().includes(term) ||
          (u.name || '').toLowerCase().includes(term) ||
          (this.notesLabel(u.notes) || '').toLowerCase().includes(term),
      );
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

  /** Whether the connected PoracleNG has trainer game accounts; the row button is absent otherwise. */
  readonly playerAccountsSupported = computed(() => this.poracleConfig.serverConfig().playerAccountsSupported);

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
          error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_ALARMS'),
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
          error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_DELETE'),
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
        error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_IMPERSONATE'),
        next: res => {
          this.auth.impersonate(res.token);
        },
      });
  }

  /**
   * Whether this row is the signed-in admin's own account. The API refuses to block (#613) or delete it,
   * so the row does not offer either. Admin pages are unreachable while impersonating, so this is the
   * admin's real id.
   */
  isSelf(user: AdminUser): boolean {
    return user.id === this.auth.user()?.id;
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
    this.poracleConfig.load().subscribe();
    this.loadUsers();
  }

  /**
   * Normalizes the Poracle `notes` value for display. PoracleJS/NG can leave a quoted-empty
   * sentinel (`""`) or whitespace in the column for users that aren't channels — those should
   * render nothing. Strips a single layer of surrounding quotes so a JSON-quoted note shows clean.
   */
  notesLabel(notes: string | null): string | null {
    if (!notes) return null;
    let s = notes.trim();
    if (s.length >= 2 && ((s.startsWith('"') && s.endsWith('"')) || (s.startsWith("'") && s.endsWith("'")))) {
      s = s.slice(1, -1).trim();
    }
    return s.length > 0 ? s : null;
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

  /** Manage another human's linked game accounts. An administrator may always, whatever self-service says. */
  openGameAccounts(user: AdminUser): void {
    this.dialog.open(GameAccountsDialogComponent, {
      width: '520px',
      autoFocus: false,
      data: { displayName: user.name || user.id, userId: user.id },
    });
  }

  pauseUser(user: AdminUser): void {
    this.adminService
      .pauseUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_STOP'),
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
        error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_START'),
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
      error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_BLOCK'),
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
        return user.currentProfileNo ?? 1;
      case 'disabledSince':
        return user.disabledDate ? new Date(user.disabledDate).getTime() : 0;
      default:
        return '';
    }
  }

  /** The server's own reason when it gave one ("You cannot block your own account."), else the generic message. */
  private showFailure(err: unknown, fallbackKey: string): void {
    this.snackBar.open(serverReason(err) ?? this.i18n.instant(fallbackKey), this.i18n.instant('TOAST.OK'), { duration: 4000 });
  }
}
