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
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';

// ─── Add Webhook Dialog ───────────────────────────────────────────────────────

@Component({
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, TranslateModule],
  selector: 'app-add-webhook-dialog',
  standalone: true,
  styleUrl: './add-webhook-dialog.component.scss',
  templateUrl: './add-webhook-dialog.component.html',
})
export class AddWebhookDialogComponent {
  private readonly fb = inject(FormBuilder);
  readonly dialogRef = inject(MatDialogRef<AddWebhookDialogComponent>);

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

// ─── Webhook Delegates Dialog ─────────────────────────────────────────────────

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
    TranslateModule,
  ],
  selector: 'app-webhook-delegates-dialog',
  standalone: true,
  styleUrl: './webhook-delegates-dialog.component.scss',
  templateUrl: './webhook-delegates-dialog.component.html',
})
export class WebhookDelegatesDialogComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<DelegatesDialogData>(MAT_DIALOG_DATA);
  readonly delegates = signal<string[]>([]);

  readonly dialogRef = inject(MatDialogRef<WebhookDelegatesDialogComponent>);
  readonly filteredUsers = signal<AdminUser[]>([]);
  readonly loading = signal(true);
  searchText = '';

  private get users(): AdminUser[] {
    return this.data.allUsers.filter(u => u.type !== 'webhook');
  }

  addDelegate(userId: string): void {
    this.adminService.addWebhookDelegate(this.data.webhook.id, userId).subscribe({
      error: () =>
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_ADD_DELEGATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
      next: d => {
        this.delegates.set(d);
        this.searchText = '';
        this.filteredUsers.set([]);
      },
    });
  }

  addDelegateFromSearch(): void {
    const uid = this.searchText.trim();
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
      error: () =>
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_REMOVE_DELEGATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
      next: d => this.delegates.set(d),
    });
  }
}

// ─── Webhooks Page ────────────────────────────────────────────────────────────

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
    MatChipsModule,
    TranslateModule,
  ],
  selector: 'app-admin-webhooks',
  standalone: true,
  styleUrl: './admin-webhooks.component.scss',
  templateUrl: './admin-webhooks.component.html',
})
export class AdminWebhooksComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly allUsers = signal<AdminUser[]>([]);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);

  private readonly poracleAdmins = signal<string[]>([]);
  private readonly porocleDelegates = signal<Record<string, string[]>>({});
  private readonly snackBar = inject(MatSnackBar);
  private readonly webhookDelegates = signal<Record<string, string[]>>({});
  private readonly webhookUsers = computed(() => this.allUsers().filter(u => u.type === 'webhook'));

  readonly searchTerm = signal('');
  readonly sortActive = signal('');
  readonly sortDirection = signal<'asc' | 'desc' | ''>('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly filteredWebhooks = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const status = this.statusFilter();
    let users = this.webhookUsers();

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
      const valA =
        active === 'id' ? a.id : active === 'name' ? (a.name || '').toLowerCase() : a.adminDisable === 1 ? 2 : a.enabled === 0 ? 1 : 0;
      const valB =
        active === 'id' ? b.id : active === 'name' ? (b.name || '').toLowerCase() : b.adminDisable === 1 ? 2 : b.enabled === 0 ? 1 : 0;
      if (valA < valB) return -1 * dir;
      if (valA > valB) return 1 * dir;
      return 0;
    });
  });

  readonly pageIndex = signal(0);
  readonly pageSize = signal(25);

  readonly paginatedWebhooks = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.filteredWebhooks().slice(start, start + this.pageSize());
  });

  readonly usersLoading = signal(true);

  readonly webhookColumns = ['name', 'id', 'enabled', 'delegates', 'actions'];

  copyUrl(url: string): void {
    navigator.clipboard.writeText(url).then(() => {
      this.snackBar.open(this.i18n.instant('ADMIN.SNACK_URL_COPIED'), undefined, { duration: 2000 });
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

  deleteWebhook(user: AdminUser): void {
    const name = user.name || user.id;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.DELETE_WEBHOOK'),
        message: this.i18n.instant('ADMIN.CONFIRM_DELETE_WEBHOOK', { name }),
        title: this.i18n.instant('ADMIN.DELETE_WEBHOOK'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUser(user.id).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_DELETE_WEBHOOK'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.allUsers.update(users => users.filter(u => u.id !== user.id));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_WEBHOOK_DELETED', { name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
        });
      }
    });
  }

  getDelegateAvatarUrl(userId: string): string | null {
    return this.allUsers().find(u => u.id === userId)?.avatarUrl ?? null;
  }

  getDelegateName(userId: string): string {
    const user = this.allUsers().find(u => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  getDelegateUserIds(webhookId: string): string[] {
    const db = this.webhookDelegates()[webhookId] ?? [];
    const admins = this.poracleAdmins();
    const configDelegates = this.porocleDelegates()[webhookId] ?? [];
    return [...new Set([...admins, ...configDelegates, ...db])];
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
        error: () =>
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
        next: res => this.auth.impersonate(res.token),
      });
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
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

  openAddDialog(): void {
    const ref = this.dialog.open(AddWebhookDialogComponent, { width: '480px' });
    ref.afterClosed().subscribe(result => {
      if (result) {
        this.adminService
          .createWebhook(result.name, result.url)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: err => {
              const msg = err?.error?.error ?? this.i18n.instant('ADMIN.SNACK_FAILED_ADD_WEBHOOK');
              this.snackBar.open(msg, this.i18n.instant('TOAST.OK'), { duration: 4000 });
            },
            next: () => {
              this.snackBar.open(this.i18n.instant('ADMIN.SNACK_WEBHOOK_ADDED', { name: result.name }), this.i18n.instant('TOAST.OK'), {
                duration: 3000,
              });
              this.loadUsers();
            },
          });
      }
    });
  }

  openDelegatesDialog(webhook: AdminUser): void {
    const ref = this.dialog.open(WebhookDelegatesDialogComponent, {
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

  resumeWebhook(user: AdminUser): void {
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

  toggleBlock(user: AdminUser, unblock: boolean): void {
    const action$ = unblock ? this.adminService.enableUser(user.id) : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_BLOCK'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      },
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
}
