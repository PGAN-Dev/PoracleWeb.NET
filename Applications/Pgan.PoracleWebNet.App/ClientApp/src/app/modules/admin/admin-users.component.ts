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
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

import { AdminListState, StatusFilter, notesLabel } from './admin-list-state';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DiscordAvatarComponent } from '../../shared/components/discord-avatar/discord-avatar.component';
import { relativeTime } from '../../shared/utils/relative-time';
import { serverReason } from '../../shared/utils/server-reason';

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
    RouterLink,
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
  private readonly i18n = inject(I18nService);

  // Everyone who is a person, so this tab and the Destinations tab together account for every
  // account. A Telegram user is a person too: filtering to discord* once left them in neither list,
  // and every admin action here is list-driven. See #632 and #1025.
  private readonly people = computed(() => this.allUsers().filter(u => u.kind === 'person'));

  private readonly snackBar = inject(MatSnackBar);

  readonly list = new AdminListState(this.people, 'admin_', (user, column) => this.getSortValue(user, column));
  readonly filteredUsers = this.list.filtered;
  readonly pageIndex = this.list.pageIndex;
  readonly pageSize = this.list.pageSize;
  readonly paginatedUsers = this.list.page;
  readonly searchTerm = this.list.searchTerm;
  readonly sortActive = this.list.sortActive;
  readonly sortDirection = this.list.sortDirection;
  readonly statusFilter = this.list.statusFilter;

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

  /** In the display language, as Destinations does; this used to build "2mo ago" in English on every page. */
  getRelativeDate(dateStr: string): string {
    const then = new Date(dateStr).getTime();
    return isNaN(then) ? '—' : relativeTime(then, Date.now(), this.i18n.currentLang());
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
    this.loadUsers();
  }

  notesLabel(notes: string | null): string | null {
    return notesLabel(notes);
  }

  onPageChange(event: PageEvent): void {
    this.list.setPage(event.pageIndex, event.pageSize);
  }

  onSearchChange(term: string): void {
    this.searchTerm.set(term);
    this.list.resetPage();
  }

  onSortChange(sort: Sort): void {
    this.sortActive.set(sort.active);
    this.sortDirection.set(sort.direction);
    this.list.resetPage();
  }

  onStatusFilterChange(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.list.resetPage();
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
