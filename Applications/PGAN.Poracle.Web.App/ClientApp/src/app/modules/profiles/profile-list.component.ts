import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatBadgeModule } from '@angular/material/badge';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ProfileService } from '../../core/services/profile.service';
import { AuthService } from '../../core/services/auth.service';
import { Profile } from '../../core/models';
import { ProfileAddDialogComponent } from './profile-add-dialog.component';
import { ProfileEditDialogComponent } from './profile-edit-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-profile-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatBadgeModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatTooltipModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Profiles</h1>
        <p class="page-description">Manage multiple alarm profiles. Each profile has its own set of alarms, areas, and location. Switch between profiles to use different alarm configurations. Your active profile is highlighted.</p>
      </div>
      <div class="header-actions">
        <button
          mat-fab
          color="primary"
          (click)="openAddDialog()"
        >
          <mat-icon>add</mat-icon>
        </button>
      </div>
    </div>

    @if (loading()) {
      <div class="loading-container">
        <mat-spinner diameter="48"></mat-spinner>
      </div>
    } @else {
      <div class="profile-grid">
        @for (profile of profiles(); track profile.profileNo) {
          <mat-card
            class="profile-card"
            [class.active-profile]="profile.active"
          >
            @if (profile.active) {
              <div class="active-badge">
                <mat-icon>check_circle</mat-icon>
                <span>Active</span>
              </div>
            }
            <mat-card-header>
              <mat-icon mat-card-avatar class="profile-avatar">person</mat-icon>
              <mat-card-title>{{ profile.name }}</mat-card-title>
              <mat-card-subtitle>Profile #{{ profile.profileNo }}</mat-card-subtitle>
            </mat-card-header>
            <mat-card-content>
              <div class="profile-details">
                <div class="detail-row">
                  <mat-icon>tag</mat-icon>
                  <span>Profile Number: {{ profile.profileNo }}</span>
                </div>
              </div>
            </mat-card-content>
            <mat-card-actions align="end">
              @if (!profile.active) {
                <button
                  mat-raised-button
                  color="primary"
                  (click)="switchProfile(profile)"
                  [disabled]="switching()"
                  matTooltip="Switch to this profile"
                >
                  <mat-icon>swap_horiz</mat-icon> Switch
                </button>
              }
              <button
                mat-icon-button
                (click)="editProfile(profile)"
                matTooltip="Edit profile name"
              >
                <mat-icon>edit</mat-icon>
              </button>
              <button
                mat-icon-button
                color="warn"
                (click)="deleteProfile(profile)"
                [disabled]="profile.active"
                [matTooltip]="profile.active ? 'Cannot delete active profile' : 'Delete profile'"
              >
                <mat-icon>delete</mat-icon>
              </button>
            </mat-card-actions>
          </mat-card>
        } @empty {
          <div class="empty-state">
            <svg viewBox="0 0 100 100" width="72" height="72" class="empty-icon">
              <ellipse cx="50" cy="72" rx="30" ry="8" fill="#7b1fa2" opacity="0.1"/>
              <circle cx="50" cy="48" r="22" fill="#7b1fa2" opacity="0.1" stroke="#7b1fa2" stroke-width="3"/>
              <path d="M28 45 Q35 20 50 18 Q65 20 72 45" fill="#7b1fa2" opacity="0.2" stroke="#7b1fa2" stroke-width="2.5"/>
              <path d="M28 45 H72" stroke="#7b1fa2" stroke-width="3"/>
              <circle cx="50" cy="38" r="5" fill="#7b1fa2" opacity="0.4"/>
              <path d="M35 56 Q50 68 65 56" fill="none" stroke="#7b1fa2" stroke-width="2" opacity="0.3"/>
            </svg>
            <h2>No Profiles</h2>
            <p>Each profile has its own set of alarms, areas, and location</p>
            <button mat-flat-button class="cta-button" (click)="openAddDialog()">
              <mat-icon>add</mat-icon> Create Profile
            </button>
          </div>
        }
      </div>
    }
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
      .header-actions {
        display: flex;
        align-items: center;
        gap: 8px;
      }
      .loading-container {
        display: flex;
        justify-content: center;
        padding: 64px;
      }
      .profile-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
        gap: 24px;
        padding: 0 24px 24px;
      }
      .profile-card {
        position: relative;
        border-top: 4px solid #9e9e9e;
        border-left: 4px solid transparent;
        transition:
          transform 0.2s,
          box-shadow 0.2s;
      }
      .profile-card:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12);
      }
      .active-profile {
        border-top-color: #4caf50;
        border-left-color: #4caf50;
      }
      .active-badge {
        position: absolute;
        top: 8px;
        right: 12px;
        display: flex;
        align-items: center;
        gap: 4px;
        color: #4caf50;
        font-size: 13px;
        font-weight: 500;
      }
      .active-badge mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
      }
      .profile-avatar {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .profile-details {
        margin-top: 8px;
      }
      .detail-row {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 4px 0;
        font-size: 14px;
        color: var(--text-muted, rgba(0, 0, 0, 0.64));
      }
      .detail-row mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
      }
      .empty-state {
        grid-column: 1 / -1;
        text-align: center;
        padding: 64px 16px;
      }
      .empty-icon {
        margin-bottom: 16px;
        opacity: 0.8;
      }
      .empty-state h2 {
        margin: 16px 0 8px;
        font-weight: 400;
      }
      .empty-state p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        margin-bottom: 24px;
      }
      .cta-button {
        background-color: #7b1fa2 !important;
        color: white !important;
      }
    `,
  ],
})
export class ProfileListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly profileService = inject(ProfileService);
  private readonly authService = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly profiles = signal<Profile[]>([]);
  readonly loading = signal(true);
  readonly switching = signal(false);

  ngOnInit(): void {
    this.loadProfiles();
  }

  loadProfiles(): void {
    this.loading.set(true);
    this.profileService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (profiles) => {
        this.profiles.set(profiles);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Failed to load profiles', 'OK', { duration: 3000 });
      },
    });
  }

  openAddDialog(): void {
    const ref = this.dialog.open(ProfileAddDialogComponent, {
      width: '400px',
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadProfiles();
    });
  }

  editProfile(profile: Profile): void {
    const ref = this.dialog.open(ProfileEditDialogComponent, {
      width: '400px',
      data: profile,
    });
    ref.afterClosed().subscribe((result) => {
      if (result) this.loadProfiles();
    });
  }

  switchProfile(profile: Profile): void {
    this.switching.set(true);
    this.profileService.switchProfile(profile.profileNo).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.switching.set(false);
        this.snackBar.open(`Switched to profile "${profile.name}"`, 'OK', {
          duration: 3000,
        });
        // Reload auth state and profiles
        this.authService.loadCurrentUser();
        this.loadProfiles();
      },
      error: () => {
        this.switching.set(false);
        this.snackBar.open('Failed to switch profile', 'OK', { duration: 3000 });
      },
    });
  }

  deleteProfile(profile: Profile): void {
    if (profile.active) return;

    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Delete Profile',
        message: `Are you sure you want to delete profile "${profile.name}" (#${profile.profileNo})? All alarms in this profile will be lost.`,
        confirmText: 'Delete',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.profileService.delete(profile.profileNo).subscribe({
          next: () => {
            this.snackBar.open('Profile deleted', 'OK', { duration: 3000 });
            this.loadProfiles();
          },
          error: () => {
            this.snackBar.open('Failed to delete profile', 'OK', { duration: 3000 });
          },
        });
      }
    });
  }
}
