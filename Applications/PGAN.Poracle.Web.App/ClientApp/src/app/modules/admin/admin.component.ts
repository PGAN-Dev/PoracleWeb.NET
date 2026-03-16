import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { AdminService } from '../../core/services/admin.service';
import { SettingsService } from '../../core/services/settings.service';
import { Human, PwebSetting } from '../../core/models';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DiscordAvatarComponent } from '../../shared/components/discord-avatar/discord-avatar.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    FormsModule,
    MatCardModule,
    MatTabsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatSlideToggleModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatDialogModule,
    MatTooltipModule,
    MatPaginatorModule,
    MatSelectModule,
    MatChipsModule,
    DiscordAvatarComponent,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Admin Panel</h1>
        <p class="page-description">Manage registered users: enable/disable accounts, view alarm counts, and manage delegated permissions.</p>
      </div>
    </div>

    <mat-tab-group class="admin-tabs" animationDuration="200ms">
      <!-- ─── User Management Tab ──────────────────────────────── -->
      <mat-tab label="User Management">
        <div class="tab-content">
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
              <table mat-table [dataSource]="paginatedUsers()" class="users-table">
                <!-- ID Column -->
                <ng-container matColumnDef="id">
                  <th mat-header-cell *matHeaderCellDef>ID</th>
                  <td mat-cell *matCellDef="let user">{{ user.id }}</td>
                </ng-container>

                <!-- Name Column -->
                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef>Name</th>
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

                <!-- Type Column -->
                <ng-container matColumnDef="type">
                  <th mat-header-cell *matHeaderCellDef>Type</th>
                  <td mat-cell *matCellDef="let user">
                    <span class="type-chip" [class]="'type-' + (user.type || 'unknown')">
                      {{ user.type || 'Unknown' }}
                    </span>
                  </td>
                </ng-container>

                <!-- Enabled Column -->
                <ng-container matColumnDef="enabled">
                  <th mat-header-cell *matHeaderCellDef>Enabled</th>
                  <td mat-cell *matCellDef="let user">
                    <mat-icon [class.enabled-icon]="user.enabled === 1" [class.disabled-icon]="user.enabled !== 1">
                      {{ user.enabled === 1 ? 'check_circle' : 'cancel' }}
                    </mat-icon>
                  </td>
                </ng-container>

                <!-- Profile Column -->
                <ng-container matColumnDef="profileNo">
                  <th mat-header-cell *matHeaderCellDef>Profile</th>
                  <td mat-cell *matCellDef="let user">{{ user.profileNo || 1 }}</td>
                </ng-container>

                <!-- Actions Column -->
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef>Actions</th>
                  <td mat-cell *matCellDef="let user">
                    <div class="action-buttons">
                      @if (user.enabled === 1) {
                        <button
                          mat-icon-button
                          color="warn"
                          (click)="toggleUser(user, false)"
                          matTooltip="Disable user"
                        >
                          <mat-icon>person_off</mat-icon>
                        </button>
                      } @else {
                        <button
                          mat-icon-button
                          color="primary"
                          (click)="toggleUser(user, true)"
                          matTooltip="Enable user"
                        >
                          <mat-icon>person</mat-icon>
                        </button>
                      }
                      <button
                        mat-icon-button
                        color="warn"
                        (click)="deleteAlarms(user)"
                        matTooltip="Delete all alarms"
                      >
                        <mat-icon>delete_sweep</mat-icon>
                      </button>
                    </div>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
              </table>
            </div>

            @if (filteredUsers().length === 0) {
              <div class="empty-state">
                <mat-icon class="empty-icon">people_outline</mat-icon>
                <h2>No users found</h2>
                <p>{{ searchTerm() ? 'Try a different search term.' : 'No users registered yet.' }}</p>
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
      </mat-tab>

      <!-- ─── Server Settings Tab ──────────────────────────────── -->
      <mat-tab label="Server Settings">
        <div class="tab-content">
          @if (settingsLoading()) {
            <div class="loading-container">
              <mat-spinner diameter="48"></mat-spinner>
            </div>
          } @else {
            <div class="settings-grid">
              @for (setting of settings(); track setting.setting) {
                <mat-card class="setting-card">
                  <mat-card-content>
                    <mat-form-field appearance="outline" class="setting-field">
                      <mat-label>{{ formatSettingLabel(setting.setting) }}</mat-label>
                      <input
                        matInput
                        [ngModel]="setting.value || ''"
                        (ngModelChange)="onSettingChange(setting, $event)"
                        [type]="isSecretSetting(setting.setting) ? 'password' : 'text'"
                        [readonly]="isReadOnlySetting(setting.setting)"
                      />
                      @if (isSecretSetting(setting.setting)) {
                        <mat-icon matSuffix>visibility_off</mat-icon>
                      }
                    </mat-form-field>
                    @if (modifiedSettings().has(setting.setting)) {
                      <button
                        mat-raised-button
                        color="primary"
                        (click)="saveSetting(setting)"
                        [disabled]="settingSaving() === setting.setting"
                        class="save-btn"
                      >
                        @if (settingSaving() === setting.setting) {
                          <mat-spinner diameter="18" class="inline-spinner"></mat-spinner>
                        } @else {
                          <mat-icon>save</mat-icon>
                        }
                        Save
                      </button>
                    }
                  </mat-card-content>
                </mat-card>
              } @empty {
                <div class="empty-state">
                  <mat-icon class="empty-icon">settings</mat-icon>
                  <h2>No settings</h2>
                  <p>No server settings have been configured yet.</p>
                </div>
              }
            </div>

            @if (settings().length > 0) {
              <div class="bulk-actions">
                <button
                  mat-raised-button
                  color="primary"
                  (click)="saveAllModified()"
                  [disabled]="modifiedSettings().size === 0 || bulkSaving()"
                >
                  @if (bulkSaving()) {
                    <mat-spinner diameter="18" class="inline-spinner"></mat-spinner>
                  } @else {
                    <mat-icon>save_all</mat-icon>
                  }
                  Save All Changes ({{ modifiedSettings().size }})
                </button>
              </div>
            }
          }
        </div>
      </mat-tab>
    </mat-tab-group>
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
      .admin-tabs {
        margin: 0 24px 24px;
      }
      .tab-content {
        padding: 24px 0;
      }
      .toolbar {
        display: flex;
        align-items: center;
        gap: 16px;
        margin-bottom: 16px;
      }
      .search-field {
        flex: 1;
        max-width: 480px;
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
      .user-name-cell {
        display: flex;
        align-items: center;
        gap: 10px;
      }
      .user-avatar {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        object-fit: cover;
        flex-shrink: 0;
      }
      .type-chip {
        display: inline-block;
        padding: 2px 10px;
        border-radius: 12px;
        font-size: 12px;
        font-weight: 500;
        text-transform: capitalize;
      }
      .type-discord {
        background: #5865f2;
        color: white;
      }
      .type-telegram {
        background: #0088cc;
        color: white;
      }
      .type-unknown {
        background: #9e9e9e;
        color: white;
      }
      .enabled-icon {
        color: #4caf50;
      }
      .disabled-icon {
        color: #f44336;
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
      .settings-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(420px, 1fr));
        gap: 16px;
      }
      .setting-card mat-card-content {
        display: flex;
        align-items: center;
        gap: 12px;
      }
      .setting-field {
        flex: 1;
      }
      .save-btn {
        white-space: nowrap;
      }
      .inline-spinner {
        display: inline-block;
        margin-right: 8px;
        vertical-align: middle;
      }
      .bulk-actions {
        display: flex;
        justify-content: flex-end;
        margin-top: 24px;
        padding-top: 16px;
        border-top: 1px solid var(--divider, rgba(0, 0, 0, 0.12));
      }
    `,
  ],
})
export class AdminComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  // ─── Users state ───────────────────────────────────
  readonly users = signal<any[]>([]);
  readonly usersLoading = signal(true);
  readonly searchTerm = signal('');
  readonly pageIndex = signal(parseInt(sessionStorage.getItem('admin_pageIndex') || '0', 10));
  readonly pageSize = signal(parseInt(sessionStorage.getItem('admin_pageSize') || '25', 10));
  readonly displayedColumns = ['id', 'name', 'type', 'enabled', 'profileNo', 'actions'];

  readonly filteredUsers = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    if (!term) return this.users();
    return this.users().filter(
      (u) =>
        u.id.toLowerCase().includes(term) ||
        (u.name || '').toLowerCase().includes(term),
    );
  });

  readonly paginatedUsers = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.filteredUsers().slice(start, start + this.pageSize());
  });

  private avatarCache = new Map<string, string>();
  private avatarFetchPending = new Set<string>();

  // ─── Settings state ────────────────────────────────
  readonly settings = signal<PwebSetting[]>([]);
  readonly settingsLoading = signal(true);
  readonly modifiedSettings = signal<Map<string, string>>(new Map());
  readonly settingSaving = signal<string | null>(null);
  readonly bulkSaving = signal(false);

  private readonly secretKeys = ['api_secret', 'scanner_db_password'];
  private readonly readOnlyKeys = ['api_address'];

  ngOnInit(): void {
    this.loadUsers();
    this.loadSettings();
  }

  // ─── User Management ──────────────────────────────
  loadUsers(): void {
    this.usersLoading.set(true);
    this.adminService.getUsers().subscribe({
      next: (users) => {
        this.users.set(users);
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

  onPageChange(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    sessionStorage.setItem('admin_pageIndex', String(event.pageIndex));
    sessionStorage.setItem('admin_pageSize', String(event.pageSize));
  }

  loadAvatarsForCurrentPage(): void {
    const page = this.paginatedUsers();
    const discordIds = page
      .filter((u: any) => u.type?.startsWith('discord') && !this.avatarCache.has(u.id) && !this.avatarFetchPending.has(u.id))
      .map((u: any) => u.id);

    if (discordIds.length === 0) return;

    for (const id of discordIds) this.avatarFetchPending.add(id);

    this.adminService.fetchAvatars(discordIds).subscribe({
      next: (avatarMap) => {
        for (const [id, url] of Object.entries(avatarMap)) {
          this.avatarCache.set(id, url);
        }
        for (const id of discordIds) this.avatarFetchPending.delete(id);
        // Rebuild users array to trigger change detection
        this.users.update(users => users.map(u =>
          this.avatarCache.has(u.id) ? { ...u, avatarUrl: this.avatarCache.get(u.id) } : u
        ));
      },
      error: () => {
        for (const id of discordIds) this.avatarFetchPending.delete(id);
      },
    });
  }

  toggleUser(user: Human, enable: boolean): void {
    const action$ = enable
      ? this.adminService.enableUser(user.id)
      : this.adminService.disableUser(user.id);

    action$.subscribe({
      next: (updated) => {
        this.users.update((users) =>
          users.map((u) => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)),
        );
        this.snackBar.open(
          `User "${user.name || user.id}" ${enable ? 'enabled' : 'disabled'}`,
          'OK',
          { duration: 3000 },
        );
      },
      error: () => {
        this.snackBar.open(`Failed to ${enable ? 'enable' : 'disable'} user`, 'OK', {
          duration: 3000,
        });
      },
    });
  }

  deleteAlarms(user: Human): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete All Alarms',
        message: `Are you sure you want to delete ALL alarms for user "${user.name || user.id}"? This action cannot be undone.`,
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

  // ─── Settings Management ──────────────────────────
  loadSettings(): void {
    this.settingsLoading.set(true);
    this.settingsService.getAll().subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.settingsLoading.set(false);
      },
      error: () => {
        this.settingsLoading.set(false);
        this.snackBar.open('Failed to load settings', 'OK', { duration: 3000 });
      },
    });
  }

  formatSettingLabel(key: string): string {
    return key
      .replace(/_/g, ' ')
      .replace(/\b\w/g, (c) => c.toUpperCase());
  }

  isSecretSetting(key: string): boolean {
    return this.secretKeys.includes(key.toLowerCase());
  }

  isReadOnlySetting(key: string): boolean {
    return this.readOnlyKeys.includes(key.toLowerCase());
  }

  onSettingChange(setting: PwebSetting, newValue: string): void {
    this.modifiedSettings.update((map) => {
      const updated = new Map(map);
      updated.set(setting.setting, newValue);
      return updated;
    });
    // Also update local state for display
    this.settings.update((list) =>
      list.map((s) =>
        s.setting === setting.setting ? { ...s, value: newValue } : s,
      ),
    );
  }

  saveSetting(setting: PwebSetting): void {
    const newValue = this.modifiedSettings().get(setting.setting);
    if (newValue === undefined) return;

    this.settingSaving.set(setting.setting);
    this.settingsService.update(setting.setting, newValue).subscribe({
      next: () => {
        this.settingSaving.set(null);
        this.modifiedSettings.update((map) => {
          const updated = new Map(map);
          updated.delete(setting.setting);
          return updated;
        });
        this.snackBar.open(`Setting "${this.formatSettingLabel(setting.setting)}" saved`, 'OK', {
          duration: 3000,
        });
      },
      error: () => {
        this.settingSaving.set(null);
        this.snackBar.open('Failed to save setting', 'OK', { duration: 3000 });
      },
    });
  }

  saveAllModified(): void {
    const entries = Array.from(this.modifiedSettings().entries());
    if (entries.length === 0) return;

    this.bulkSaving.set(true);
    let completed = 0;
    let errors = 0;

    for (const [key, value] of entries) {
      this.settingsService.update(key, value).subscribe({
        next: () => {
          completed++;
          this.modifiedSettings.update((map) => {
            const updated = new Map(map);
            updated.delete(key);
            return updated;
          });
          if (completed + errors === entries.length) {
            this.bulkSaving.set(false);
            if (errors === 0) {
              this.snackBar.open(`All ${completed} setting(s) saved`, 'OK', { duration: 3000 });
            } else {
              this.snackBar.open(`${completed} saved, ${errors} failed`, 'OK', { duration: 5000 });
            }
          }
        },
        error: () => {
          errors++;
          if (completed + errors === entries.length) {
            this.bulkSaving.set(false);
            this.snackBar.open(`${completed} saved, ${errors} failed`, 'OK', { duration: 5000 });
          }
        },
      });
    }
  }
}
