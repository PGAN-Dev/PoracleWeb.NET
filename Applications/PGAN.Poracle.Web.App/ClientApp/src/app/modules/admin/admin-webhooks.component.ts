import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { AdminUser } from '../../core/models';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';

// ─── Add Webhook Dialog ───────────────────────────────────────────────────────

@Component({
  selector: 'app-add-webhook-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>Add Webhook</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="webhook-form">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" placeholder="e.g. #pogo-alerts" />
          @if (form.get('name')?.hasError('required') && form.get('name')?.touched) {
            <mat-error>Name is required</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Webhook URL</mat-label>
          <input matInput formControlName="url" placeholder="https://discord.com/api/webhooks/..." />
          @if (form.get('url')?.hasError('required') && form.get('url')?.touched) {
            <mat-error>URL is required</mat-error>
          }
          @if (form.get('url')?.hasError('pattern') && form.get('url')?.touched) {
            <mat-error>Must be a valid https:// URL</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancel</button>
      <button mat-raised-button color="primary" [disabled]="form.invalid" (click)="submit()">
        <mat-icon>add</mat-icon> Add Webhook
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .webhook-form { display: flex; flex-direction: column; gap: 8px; padding-top: 8px; min-width: 400px; }
    .full-width { width: 100%; }
  `],
})
export class AddWebhookDialogComponent {
  readonly dialogRef = inject(MatDialogRef<AddWebhookDialogComponent>);
  private readonly fb = inject(FormBuilder);

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
  webhook: AdminUser;
  poracleAdmins: string[];
  porocleDelegates: string[];
  allUsers: AdminUser[];
}

@Component({
  selector: 'app-webhook-delegates-dialog',
  standalone: true,
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
  ],
  template: `
    <h2 mat-dialog-title>Manage Delegates — {{ data.webhook.name || data.webhook.id }}</h2>
    <mat-dialog-content>
      <p class="hint">Delegates can manage alarms for this webhook. Search by name or Discord user ID.</p>
      @if (loading()) {
        <mat-spinner diameter="32"></mat-spinner>
      } @else {
        <mat-chip-set class="delegates-chips">
          @for (uid of data.poracleAdmins; track uid) {
            <mat-chip class="admin-chip" [matTooltip]="'Global admin (from Poracle config)'">
              @if (getAvatarUrl(uid)) {
                <img matChipAvatar [src]="getAvatarUrl(uid)!" [alt]="getDisplayName(uid)" />
              }
              {{ getDisplayName(uid) }}
              <mat-icon matChipTrailingIcon class="admin-lock-icon">lock</mat-icon>
            </mat-chip>
          }
          @for (uid of data.porocleDelegates; track uid) {
            <mat-chip class="admin-chip" [matTooltip]="'Delegate from Poracle config (read-only)'">
              @if (getAvatarUrl(uid)) {
                <img matChipAvatar [src]="getAvatarUrl(uid)!" [alt]="getDisplayName(uid)" />
              }
              {{ getDisplayName(uid) }}
              <mat-icon matChipTrailingIcon class="admin-lock-icon">lock</mat-icon>
            </mat-chip>
          }
          @for (uid of delegates(); track uid) {
            <mat-chip (removed)="removeDelegate(uid)">
              @if (getAvatarUrl(uid)) {
                <img matChipAvatar [src]="getAvatarUrl(uid)!" [alt]="getDisplayName(uid)" />
              }
              {{ getDisplayName(uid) }}
              <button matChipRemove aria-label="Remove delegate">
                <mat-icon>cancel</mat-icon>
              </button>
            </mat-chip>
          }
        </mat-chip-set>
        <div class="add-row">
          <mat-form-field appearance="outline" class="user-id-field">
            <mat-label>Search user by name or ID</mat-label>
            <input
              matInput
              [(ngModel)]="searchText"
              (ngModelChange)="onSearchChange($event)"
              (keydown.enter)="addDelegateFromSearch()"
              [matAutocomplete]="userAuto"
              placeholder="Name or Discord user ID..."
            />
            <mat-autocomplete #userAuto="matAutocomplete" (optionSelected)="onUserSelected($event.option.value)">
              @for (u of filteredUsers(); track u.id) {
                <mat-option [value]="u.id">
                  <div class="user-option">
                    @if (u.avatarUrl) {
                      <img [src]="u.avatarUrl" class="option-avatar" alt="" />
                    } @else {
                      <mat-icon class="option-avatar-icon">account_circle</mat-icon>
                    }
                    <span class="option-name">{{ u.name || u.id }}</span>
                    <span class="option-id">{{ u.id }}</span>
                  </div>
                </mat-option>
              }
            </mat-autocomplete>
          </mat-form-field>
          <button mat-raised-button color="primary" [disabled]="!searchText.trim()" (click)="addDelegateFromSearch()">
            <mat-icon>person_add</mat-icon> Add
          </button>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { color: rgba(0,0,0,0.54); font-size: 13px; margin-bottom: 12px; }
    .delegates-chips { display: block; margin-bottom: 16px; min-height: 32px; }
    .add-row { display: flex; gap: 12px; align-items: center; }
    .user-id-field { flex: 1; }
    .admin-chip { opacity: 0.8; }
    .admin-lock-icon { font-size: 14px; width: 14px; height: 14px; color: rgba(0,0,0,0.4); }
    .user-option { display: flex; align-items: center; gap: 8px; line-height: 1; }
    .option-avatar { width: 28px; height: 28px; border-radius: 50%; flex-shrink: 0; display: block; }
    .option-avatar-icon { font-size: 28px; width: 28px; height: 28px; flex-shrink: 0; color: rgba(0,0,0,0.38); line-height: 28px; }
    .option-name { font-weight: 500; margin-right: 4px; }
    .option-id { font-size: 11px; color: rgba(0,0,0,0.54); font-family: monospace; }
  `],
})
export class WebhookDelegatesDialogComponent implements OnInit {
  readonly dialogRef = inject(MatDialogRef<WebhookDelegatesDialogComponent>);
  readonly data = inject<DelegatesDialogData>(MAT_DIALOG_DATA);
  private readonly adminService = inject(AdminService);
  private readonly snackBar = inject(MatSnackBar);

  readonly delegates = signal<string[]>([]);
  readonly loading = signal(true);
  readonly filteredUsers = signal<AdminUser[]>([]);
  searchText = '';

  private get users(): AdminUser[] {
    return this.data.allUsers.filter((u) => u.type !== 'webhook');
  }

  ngOnInit(): void {
    this.adminService.getWebhookDelegates(this.data.webhook.id).subscribe({
      next: (d) => { this.delegates.set(d); this.loading.set(false); },
      error: () => { this.loading.set(false); this.snackBar.open('Failed to load delegates', 'OK', { duration: 3000 }); },
    });
  }

  onSearchChange(term: string): void {
    const t = term.toLowerCase().trim();
    if (!t) { this.filteredUsers.set([]); return; }
    this.filteredUsers.set(
      this.users
        .filter((u) => u.id.includes(t) || (u.name || '').toLowerCase().includes(t))
        .slice(0, 10),
    );
  }

  onUserSelected(userId: string): void {
    this.searchText = userId;
    this.filteredUsers.set([]);
    this.addDelegate(userId);
  }

  addDelegateFromSearch(): void {
    const uid = this.searchText.trim();
    if (!uid) return;
    this.addDelegate(uid);
  }

  addDelegate(userId: string): void {
    this.adminService.addWebhookDelegate(this.data.webhook.id, userId).subscribe({
      next: (d) => { this.delegates.set(d); this.searchText = ''; this.filteredUsers.set([]); },
      error: () => this.snackBar.open('Failed to add delegate', 'OK', { duration: 3000 }),
    });
  }

  removeDelegate(userId: string): void {
    this.adminService.removeWebhookDelegate(this.data.webhook.id, userId).subscribe({
      next: (d) => this.delegates.set(d),
      error: () => this.snackBar.open('Failed to remove delegate', 'OK', { duration: 3000 }),
    });
  }

  getDisplayName(userId: string): string {
    const user = this.users.find((u) => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  getAvatarUrl(userId: string): string | null {
    return this.users.find((u) => u.id === userId)?.avatarUrl ?? null;
  }
}

// ─── Webhooks Page ────────────────────────────────────────────────────────────

type StatusFilter = 'all' | 'active' | 'stopped' | 'blocked';

@Component({
  selector: 'app-admin-webhooks',
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
    MatChipsModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Webhooks</h1>
        <p class="page-description">Manage registered webhook destinations: block/unblock, stop/start alerts, and delete alarms.</p>
      </div>
      <button mat-raised-button color="primary" (click)="openAddDialog()">
        <mat-icon>add</mat-icon> Add Webhook
      </button>
    </div>

    <div class="page-content">
      <div class="toolbar">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>Search webhooks</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input
            matInput
            [ngModel]="searchTerm()"
            (ngModelChange)="onSearchChange($event)"
            placeholder="Filter by name or URL..."
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
        <button mat-raised-button (click)="loadUsers()">
          <mat-icon>refresh</mat-icon> Refresh
        </button>
      </div>

      @if (usersLoading()) {
        <div class="loading-container">
          <mat-spinner diameter="48"></mat-spinner>
        </div>
      } @else {
        <div class="table-container">
          <table mat-table matSort (matSortChange)="onSortChange($event)" [dataSource]="paginatedWebhooks()" class="users-table">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
              <td mat-cell *matCellDef="let wh">{{ wh.name || '(unnamed)' }}</td>
            </ng-container>

            <ng-container matColumnDef="id">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>URL</th>
              <td mat-cell *matCellDef="let wh">
                <span
                  class="webhook-url"
                  [matTooltip]="wh.id"
                  matTooltipClass="url-tooltip"
                  (click)="copyUrl(wh.id)"
                  title="Click to copy"
                >
                  <mat-icon class="copy-icon">content_copy</mat-icon>{{ truncateUrl(wh.id) }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="enabled">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
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

            <ng-container matColumnDef="delegates">
              <th mat-header-cell *matHeaderCellDef>Delegates</th>
              <td mat-cell *matCellDef="let wh">
                <div class="delegate-avatars">
                  @for (uid of getDelegateUserIds(wh.id); track uid) {
                    <div
                      class="delegate-avatar-wrap"
                      [matTooltip]="getDelegateName(uid) + (isPoracleAdmin(uid) ? ' (global admin)' : isPorocleDelegateForWebhook(wh.id, uid) ? ' (config delegate)' : '')"
                    >
                      @if (getDelegateAvatarUrl(uid)) {
                        <img class="delegate-avatar" [src]="getDelegateAvatarUrl(uid)!" alt="" />
                      } @else {
                        <div class="delegate-avatar delegate-avatar-fallback">
                          <mat-icon>account_circle</mat-icon>
                        </div>
                      }
                      @if (isLockedDelegate(wh.id, uid)) {
                        <mat-icon class="delegate-lock">lock</mat-icon>
                      }
                    </div>
                  }
                </div>
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let wh">
                <div class="action-buttons">
                  @if (wh.adminDisable !== 1) {
                    <button mat-icon-button color="warn" (click)="toggleBlock(wh, false)" matTooltip="Block webhook">
                      <mat-icon>block</mat-icon>
                    </button>
                  } @else {
                    <button mat-icon-button color="primary" (click)="toggleBlock(wh, true)" matTooltip="Unblock webhook">
                      <mat-icon>check_circle</mat-icon>
                    </button>
                  }
                  @if (wh.enabled === 0) {
                    <button mat-icon-button color="primary" (click)="resumeWebhook(wh)" matTooltip="Start alerts">
                      <mat-icon>play_arrow</mat-icon>
                    </button>
                  } @else {
                    <button mat-icon-button (click)="pauseWebhook(wh)" matTooltip="Stop alerts">
                      <mat-icon>pause</mat-icon>
                    </button>
                  }
                  <button mat-icon-button (click)="manageAlarms(wh)" matTooltip="Manage alarms">
                    <mat-icon>tune</mat-icon>
                  </button>
                  <button mat-icon-button (click)="openDelegatesDialog(wh)" matTooltip="Manage delegates">
                    <mat-icon>group_add</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" (click)="deleteAlarms(wh)" matTooltip="Delete all alarms">
                    <mat-icon>delete_sweep</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" (click)="deleteWebhook(wh)" matTooltip="Delete webhook">
                    <mat-icon>person_remove</mat-icon>
                  </button>
                </div>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="webhookColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: webhookColumns;"></tr>
          </table>
        </div>

        @if (filteredWebhooks().length === 0) {
          <div class="empty-state">
            <mat-icon class="empty-icon">webhook</mat-icon>
            <h2>No webhooks found</h2>
            <p>{{ searchTerm() || statusFilter() !== 'all' ? 'Try adjusting your filters.' : 'No webhooks registered.' }}</p>
          </div>
        }

        @if (filteredWebhooks().length > 0) {
          <mat-paginator
            [length]="filteredWebhooks().length"
            [pageSize]="pageSize()"
            [pageIndex]="pageIndex()"
            [pageSizeOptions]="[10, 25, 50]"
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
      .webhook-url {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        font-family: monospace;
        font-size: 12px;
        color: var(--text-secondary, rgba(0,0,0,0.6));
        cursor: pointer;
        padding: 2px 6px;
        border-radius: 4px;
        transition: background 0.15s;
      }
      .webhook-url:hover {
        background: rgba(25, 118, 210, 0.08);
        color: #1976d2;
      }
      .copy-icon {
        font-size: 14px;
        width: 14px;
        height: 14px;
        opacity: 0.5;
      }
      .webhook-url:hover .copy-icon {
        opacity: 1;
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
      .action-buttons { display: flex; gap: 4px; }
      .delegate-avatars { display: flex; align-items: center; }
      .delegate-avatar-wrap {
        position: relative;
        width: 24px; height: 24px;
        margin-left: -6px; flex-shrink: 0;
      }
      .delegate-avatar-wrap:first-child { margin-left: 0; }
      .delegate-avatar {
        width: 24px; height: 24px; border-radius: 50%;
        border: 2px solid var(--mat-sidenav-container-background-color, white);
        box-shadow: 0 0 0 1px rgba(0,0,0,0.15);
        object-fit: cover; display: block;
      }
      .delegate-avatar-fallback {
        background: rgba(0,0,0,0.12);
        display: flex; align-items: center; justify-content: center;
        border-radius: 50%; width: 24px; height: 24px;
      }
      .delegate-avatar-fallback mat-icon { font-size: 18px; width: 18px; height: 18px; color: rgba(0,0,0,0.4); }
      .delegate-lock {
        position: absolute; bottom: -3px; right: -3px;
        font-size: 10px; width: 10px; height: 10px;
        background: white; border-radius: 50%; color: #555;
        line-height: 10px;
      }
      .empty-state { text-align: center; padding: 64px 16px; }
      .empty-icon {
        font-size: 64px; width: 64px; height: 64px;
        color: var(--text-hint, rgba(0, 0, 0, 0.24));
      }
      .empty-state h2 { margin: 16px 0 8px; font-weight: 400; }
      .empty-state p { color: var(--text-secondary, rgba(0, 0, 0, 0.54)); }
      @media (max-width: 599px) {
        .table-container {
          overflow-x: auto;
          -webkit-overflow-scrolling: touch;
        }
        table {
          min-width: 700px;
        }
      }
    `,
  ],
})
export class AdminWebhooksComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  private readonly allUsers = signal<AdminUser[]>([]);
  private readonly webhookDelegates = signal<Record<string, string[]>>({});
  private readonly poracleAdmins = signal<string[]>([]);
  private readonly porocleDelegates = signal<Record<string, string[]>>({});
  readonly usersLoading = signal(true);

  readonly searchTerm = signal('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly pageIndex = signal(0);
  readonly pageSize = signal(25);
  readonly webhookColumns = ['name', 'id', 'enabled', 'delegates', 'actions'];
  readonly sortActive = signal('');
  readonly sortDirection = signal<'asc' | 'desc' | ''>('');

  private readonly webhookUsers = computed(() =>
    this.allUsers().filter((u) => u.type === 'webhook'),
  );

  readonly filteredWebhooks = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const status = this.statusFilter();
    let users = this.webhookUsers();

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
      const valA =
        active === 'id' ? a.id :
        active === 'name' ? (a.name || '').toLowerCase() :
        a.adminDisable === 1 ? 2 : a.enabled === 0 ? 1 : 0;
      const valB =
        active === 'id' ? b.id :
        active === 'name' ? (b.name || '').toLowerCase() :
        b.adminDisable === 1 ? 2 : b.enabled === 0 ? 1 : 0;
      if (valA < valB) return -1 * dir;
      if (valA > valB) return 1 * dir;
      return 0;
    });
  });

  readonly paginatedWebhooks = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.filteredWebhooks().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    forkJoin({
      users: this.adminService.getUsers(),
      delegates: this.adminService.getAllWebhookDelegates(),
      poracleAdmins: this.adminService.getPoracleAdmins(),
      porocleDelegates: this.adminService.getPorocleDelegates(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ users, delegates, poracleAdmins, porocleDelegates }) => {
        this.allUsers.set(users);
        this.webhookDelegates.set(delegates);
        this.poracleAdmins.set(poracleAdmins);
        this.porocleDelegates.set(porocleDelegates);
        this.usersLoading.set(false);
      },
      error: () => {
        this.usersLoading.set(false);
        this.snackBar.open('Failed to load webhooks', 'OK', { duration: 3000 });
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

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
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

  copyUrl(url: string): void {
    navigator.clipboard.writeText(url).then(() => {
      this.snackBar.open('URL copied to clipboard', undefined, { duration: 2000 });
    });
  }

  openAddDialog(): void {
    const ref = this.dialog.open(AddWebhookDialogComponent, { width: '480px' });
    ref.afterClosed().subscribe((result) => {
      if (result) {
        this.adminService.createWebhook(result.name, result.url)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: () => {
              this.snackBar.open(`Webhook "${result.name}" added`, 'OK', { duration: 3000 });
              this.loadUsers();
            },
            error: (err) => {
              const msg = err?.error?.error ?? 'Failed to add webhook';
              this.snackBar.open(msg, 'OK', { duration: 4000 });
            },
          });
      }
    });
  }

  getDelegateUserIds(webhookId: string): string[] {
    const db = this.webhookDelegates()[webhookId] ?? [];
    const admins = this.poracleAdmins();
    const configDelegates = this.porocleDelegates()[webhookId] ?? [];
    return [...new Set([...admins, ...configDelegates, ...db])];
  }

  /** Returns true if this userId is locked (from Poracle config — not DB-managed). */
  isPoracleAdmin(userId: string): boolean {
    return this.poracleAdmins().includes(userId);
  }

  isPorocleDelegateForWebhook(webhookId: string, userId: string): boolean {
    return (this.porocleDelegates()[webhookId] ?? []).includes(userId);
  }

  isLockedDelegate(webhookId: string, userId: string): boolean {
    return this.isPoracleAdmin(userId) || this.isPorocleDelegateForWebhook(webhookId, userId);
  }

  getDelegateAvatarUrl(userId: string): string | null {
    return this.allUsers().find((u) => u.id === userId)?.avatarUrl ?? null;
  }

  getDelegateName(userId: string): string {
    const user = this.allUsers().find((u) => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  openDelegatesDialog(webhook: AdminUser): void {
    const ref = this.dialog.open(WebhookDelegatesDialogComponent, {
      width: '520px',
      data: {
        webhook,
        poracleAdmins: this.poracleAdmins(),
        porocleDelegates: this.porocleDelegates()[webhook.id] ?? [],
        allUsers: this.allUsers(),
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

  manageAlarms(user: AdminUser): void {
    this.adminService.impersonateById(user.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (res) => this.auth.impersonate(res.token),
      error: () => this.snackBar.open('Failed to switch to webhook context', 'OK', { duration: 3000 }),
    });
  }

  toggleBlock(user: AdminUser, unblock: boolean): void {
    const action$ = unblock
      ? this.adminService.enableUser(user.id)
      : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.allUsers.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, adminDisable: updated.adminDisable } : u)),
        );
        this.snackBar.open(`"${user.name || user.id}" ${unblock ? 'unblocked' : 'blocked'}`, 'OK', { duration: 3000 });
      },
      error: () => {
        this.snackBar.open(`Failed to ${unblock ? 'unblock' : 'block'}`, 'OK', { duration: 3000 });
      },
    });
  }

  pauseWebhook(user: AdminUser): void {
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

  resumeWebhook(user: AdminUser): void {
    this.adminService.resumeUser(user.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.allUsers.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)),
        );
        this.snackBar.open(`Alerts started for "${user.name || user.id}"`, 'OK', { duration: 3000 });
      },
      error: () => {
        this.snackBar.open('Failed to start alerts', 'OK', { duration: 3000 });
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
            this.snackBar.open(`Deleted ${result.deleted} alarm(s) for "${user.name || user.id}"`, 'OK', { duration: 3000 });
          },
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }

  deleteWebhook(user: AdminUser): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Webhook',
        message: `Are you sure you want to permanently delete "${user.name || user.id}"? This will remove the webhook account but NOT its alarms. Use "Delete all alarms" first if needed.`,
        confirmText: 'Delete Webhook',
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
            this.snackBar.open('Failed to delete webhook', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }
}
