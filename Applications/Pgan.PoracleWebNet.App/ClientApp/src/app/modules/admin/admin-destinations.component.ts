import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
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
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { AdminListState, StatusFilter, notesLabel } from './admin-list-state';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { isKnownKind, kindIcon, kindLabelKey } from '../../shared/utils/human-kind';
import { relativeTime } from '../../shared/utils/relative-time';
import { serverReason } from '../../shared/utils/server-reason';

// ─── Add Destination Dialog ───────────────────────────────────────────────────────

@Component({
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, TranslatePipe],
  selector: 'app-add-destination-dialog',
  standalone: true,
  styleUrl: './add-destination-dialog.component.scss',
  templateUrl: './add-destination-dialog.component.html',
})
export class AddDestinationDialogComponent {
  private readonly fb = inject(FormBuilder);
  readonly dialogRef = inject(MatDialogRef<AddDestinationDialogComponent>);

  readonly form = this.fb.group({
    name: ['', Validators.required],
    url: ['', [Validators.required, Validators.pattern(/^https?:\/\/.+/)]],
  });

  submit(): void {
    if (this.form.valid) {
      this.dialogRef.close(this.form.value);
    }
  }
}

// ─── Destination Delegates Dialog ─────────────────────────────────────────────────

interface DelegatesDialogData {
  allUsers: AdminUser[];
  poracleAdmins: string[];
  porocleDelegates: string[];
  webhook: AdminUser;
}

@Component({
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatAutocompleteModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-destination-delegates-dialog',
  standalone: true,
  styleUrl: './destination-delegates-dialog.component.scss',
  templateUrl: './destination-delegates-dialog.component.html',
})
export class DestinationDelegatesDialogComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<DelegatesDialogData>(MAT_DIALOG_DATA);
  readonly delegates = signal<string[]>([]);

  readonly dialogRef = inject(MatDialogRef<DestinationDelegatesDialogComponent>);
  readonly filteredUsers = signal<AdminUser[]>([]);
  readonly loading = signal(true);
  searchText = '';

  /**
   * The user the field names, once it names one: an option picked from the list fills in its id. Free
   * text ("Sam") used to be sent as an id and came back as a 400 behind a generic toast.
   */
  get pickedUserId(): string | null {
    const text = this.searchText.trim();
    return this.users.find(u => u.id === text)?.id ?? null;
  }

  /** People only: a channel, a group or an unknown type is nobody who could sign in and use the grant. */
  private get users(): AdminUser[] {
    return this.data.allUsers.filter(u => u.kind === 'person');
  }

  addDelegate(userId: string): void {
    this.adminService.addWebhookDelegate(this.data.webhook.id, userId).subscribe({
      error: err =>
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_ADD_DELEGATE'), this.i18n.instant('TOAST.OK'), {
          duration: 4000,
        }),
      next: d => {
        this.delegates.set(d);
        this.searchText = '';
        this.filteredUsers.set([]);
      },
    });
  }

  addDelegateFromSearch(event?: Event): void {
    // Enter on a highlighted option is the autocomplete's: it selects the option (which adds it) and
    // marks the key handled, so adding here as well would send the request twice.
    if (event?.defaultPrevented) return;
    const uid = this.pickedUserId;
    if (!uid) return;
    this.addDelegate(uid);
  }

  getAvatarUrl(userId: string): string | null {
    return this.users.find(u => u.id === userId)?.avatarUrl ?? null;
  }

  getDisplayName(userId: string): string {
    const user = this.users.find(u => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  ngOnInit(): void {
    this.adminService.getWebhookDelegates(this.data.webhook.id).subscribe({
      error: () => {
        this.loading.set(false);
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_DELEGATES'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      },
      next: d => {
        this.delegates.set(d);
        this.loading.set(false);
      },
    });
  }

  onSearchChange(term: string): void {
    const t = term.toLowerCase().trim();
    if (!t) {
      this.filteredUsers.set([]);
      return;
    }
    this.filteredUsers.set(this.users.filter(u => u.id.includes(t) || (u.name || '').toLowerCase().includes(t)).slice(0, 10));
  }

  onUserSelected(userId: string): void {
    this.searchText = userId;
    this.filteredUsers.set([]);
    this.addDelegate(userId);
  }

  removeDelegate(userId: string): void {
    this.adminService.removeWebhookDelegate(this.data.webhook.id, userId).subscribe({
      error: err =>
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_REMOVE_DELEGATE'), this.i18n.instant('TOAST.OK'), {
          duration: 4000,
        }),
      next: d => this.delegates.set(d),
    });
  }
}

// ─── Destinations Page ────────────────────────────────────────────────────────────

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
    MatChipsModule,
    TranslatePipe,
  ],
  selector: 'app-admin-destinations',
  standalone: true,
  styleUrl: './admin-destinations.component.scss',
  templateUrl: './admin-destinations.component.html',
})
export class AdminDestinationsComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly allUsers = signal<AdminUser[]>([]);
  private readonly auth = inject(AuthService);
  // Every row the server calls a destination, unknown types included, so this page and Users together
  // account for every account. See #1025.
  private readonly destinations = computed(() => this.allUsers().filter(u => u.kind === 'destination'));
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  private readonly i18n = inject(I18nService);
  private readonly poracleAdmins = signal<string[]>([]);
  private readonly porocleDelegates = signal<Record<string, string[]>>({});
  private readonly snackBar = inject(MatSnackBar);
  private readonly webhookDelegates = signal<Record<string, string[]>>({});

  readonly columns = ['name', 'type', 'id', 'enabled', 'disabledSince', 'delegates', 'actions'];

  readonly isKnownKind = isKnownKind;
  readonly kindIcon = kindIcon;
  readonly kindLabelKey = kindLabelKey;

  readonly list = new AdminListState(this.destinations, 'admin_destinations_', (row, column) => this.getSortValue(row, column));

  readonly usersLoading = signal(true);

  /** The Block button's tooltip: a channel or group can be blocked by Poracle too, which is worth knowing here. */
  blockTooltipKey(row: AdminUser): string {
    return this.isWebhook(row) ? 'ADMIN.BLOCK_DESTINATION' : 'ADMIN.BLOCKED_CHANNEL_TOOLTIP';
  }

  copyId(id: string): void {
    navigator.clipboard.writeText(id).then(() => {
      this.snackBar.open(this.i18n.instant('ADMIN.SNACK_ID_COPIED'), undefined, { duration: 2000 });
    });
  }

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

  deleteDestination(user: AdminUser): void {
    const name = user.name || user.id;
    let message = this.i18n.instant('ADMIN.CONFIRM_DELETE_DESTINATION', { name });
    if (!this.isWebhook(user)) message += ' ' + this.i18n.instant('ADMIN.DELETE_DESTINATION_NOTE');
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.DELETE_DESTINATION'),
        message,
        title: this.i18n.instant('ADMIN.DELETE_DESTINATION'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUser(user.id).subscribe({
          error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_DELETE_DESTINATION'),
          next: () => {
            this.allUsers.update(users => users.filter(u => u.id !== user.id));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_DESTINATION_DELETED', { name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
        });
      }
    });
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? '' : d.toLocaleString();
  }

  getDelegateAvatarUrl(userId: string): string | null {
    return this.allUsers().find(u => u.id === userId)?.avatarUrl ?? null;
  }

  getDelegateName(userId: string): string {
    const user = this.allUsers().find(u => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  /**
   * The real grants on a webhook: Poracle config delegates and the ones added here. Global admins can
   * manage every webhook, which the page header says once; listing them on every row buried the grants
   * someone actually made under the same avatars on every hook.
   */
  getDelegateUserIds(webhookId: string): string[] {
    const db = this.webhookDelegates()[webhookId] ?? [];
    const configDelegates = this.porocleDelegates()[webhookId] ?? [];
    return [...new Set([...configDelegates, ...db])];
  }

  isLockedDelegate(webhookId: string, userId: string): boolean {
    return this.isPoracleAdmin(userId) || this.isPorocleDelegateForWebhook(webhookId, userId);
  }

  /** Returns true if this userId is locked (from Poracle config — not DB-managed). */
  isPoracleAdmin(userId: string): boolean {
    return this.poracleAdmins().includes(userId);
  }

  isPorocleDelegateForWebhook(webhookId: string, userId: string): boolean {
    return (this.porocleDelegates()[webhookId] ?? []).includes(userId);
  }

  /** Grants and the delegates dialog are webhook-only on the server until delegation widens (#1025 part 6). */
  isWebhook(row: AdminUser): boolean {
    return (row.type ?? '').trim().toLowerCase() === 'webhook';
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    forkJoin({
      delegates: this.adminService.getAllWebhookDelegates(),
      poracleAdmins: this.adminService.getPoracleAdmins(),
      porocleDelegates: this.adminService.getPorocleDelegates(),
      users: this.adminService.getUsers(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.usersLoading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_WEBHOOKS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: ({ delegates, poracleAdmins, porocleDelegates, users }) => {
          this.allUsers.set(users);
          this.webhookDelegates.set(delegates);
          this.poracleAdmins.set(poracleAdmins);
          this.porocleDelegates.set(porocleDelegates);
          this.usersLoading.set(false);
        },
      });
  }

  manageAlarms(user: AdminUser): void {
    this.adminService
      .impersonateById(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err =>
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'), this.i18n.instant('TOAST.OK'), {
            duration: 4000,
          }),
        next: res => this.auth.impersonate(res.token),
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
    this.list.searchTerm.set(term);
    this.list.resetPage();
  }

  onSortChange(sort: Sort): void {
    this.list.sortActive.set(sort.active);
    this.list.sortDirection.set(sort.direction);
    this.list.resetPage();
  }

  onStatusFilterChange(status: StatusFilter): void {
    this.list.statusFilter.set(status);
    this.list.resetPage();
  }

  onTypeFilterChange(type: string | null | undefined): void {
    // A listbox chip clicked a second time deselects to undefined; that means "all", not nothing.
    this.list.typeFilter.set(type || 'all');
    this.list.resetPage();
  }

  openAddDialog(): void {
    const ref = this.dialog.open(AddDestinationDialogComponent, { width: '480px' });
    ref.afterClosed().subscribe(result => {
      if (result) {
        this.adminService
          .createWebhook(result.name, result.url)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: err => {
              // 502 is the #482 half-write answer ("Check the webhook list before trying again"), which must
              // reach the admin: a retry on the generic message can create a second row.
              const msg = serverReason(err, { allow5xx: [502] }) ?? this.i18n.instant('ADMIN.SNACK_FAILED_ADD_DESTINATION');
              this.snackBar.open(msg, this.i18n.instant('TOAST.OK'), { duration: 4000 });
            },
            next: () => {
              this.snackBar.open(this.i18n.instant('ADMIN.SNACK_DESTINATION_ADDED', { name: result.name }), this.i18n.instant('TOAST.OK'), {
                duration: 3000,
              });
              this.loadUsers();
            },
          });
      }
    });
  }

  openDelegatesDialog(webhook: AdminUser): void {
    const ref = this.dialog.open(DestinationDelegatesDialogComponent, {
      width: '520px',
      data: {
        allUsers: this.allUsers(),
        poracleAdmins: this.poracleAdmins(),
        porocleDelegates: this.porocleDelegates()[webhook.id] ?? [],
        webhook,
      } satisfies DelegatesDialogData,
    });
    ref.afterClosed().subscribe(() => {
      forkJoin({
        delegates: this.adminService.getAllWebhookDelegates(),
        porocleDelegates: this.adminService.getPorocleDelegates(),
      }).subscribe({
        next: ({ delegates, porocleDelegates }) => {
          this.webhookDelegates.set(delegates);
          this.porocleDelegates.set(porocleDelegates);
        },
      });
    });
  }

  pauseWebhook(user: AdminUser): void {
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

  relativeDate(dateStr: string): string {
    const then = new Date(dateStr).getTime();
    return isNaN(then) ? '—' : relativeTime(then, Date.now(), this.i18n.currentLang());
  }

  resumeWebhook(user: AdminUser): void {
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

  toggleBlock(user: AdminUser, unblock: boolean): void {
    const action$ = unblock ? this.adminService.enableUser(user.id) : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_BLOCK'),
      next: updated => {
        this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, adminDisable: updated.adminDisable } : u)));
        const name = user.name || user.id;
        this.snackBar.open(
          this.i18n.instant(unblock ? 'ADMIN.SNACK_UNBLOCKED' : 'ADMIN.SNACK_BLOCKED', { name }),
          this.i18n.instant('TOAST.OK'),
          { duration: 3000 },
        );
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

  private getSortValue(row: AdminUser, column: string): string | number {
    switch (column) {
      case 'id':
        return row.id;
      case 'name':
        return (row.name || '').toLowerCase();
      case 'type':
        return (row.type ?? '').trim().toLowerCase();
      case 'enabled':
        return row.adminDisable === 1 ? 2 : row.enabled === 0 ? 1 : 0;
      case 'disabledSince':
        return row.disabledDate ? new Date(row.disabledDate).getTime() : 0;
      default:
        return '';
    }
  }

  /** The server's own reason when it gave one, else the generic message. */
  private showFailure(err: unknown, fallbackKey: string): void {
    this.snackBar.open(serverReason(err) ?? this.i18n.instant(fallbackKey), this.i18n.instant('TOAST.OK'), { duration: 4000 });
  }
}
