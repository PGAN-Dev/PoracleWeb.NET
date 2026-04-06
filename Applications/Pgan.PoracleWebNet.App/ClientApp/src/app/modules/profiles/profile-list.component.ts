import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { ProfileAddDialogComponent } from './profile-add-dialog.component';
import { ProfileCopyDialogComponent } from './profile-copy-dialog.component';
import { ProfileEditDialogComponent } from './profile-edit-dialog.component';
import { Profile } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ProfileService } from '../../core/services/profile.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
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
  selector: 'app-profile-list',
  standalone: true,
  styleUrl: './profile-list.component.scss',
  templateUrl: './profile-list.component.html',
})
export class ProfileListComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly profiles = signal<Profile[]>([]);
  readonly switching = signal(false);

  deleteProfile(profile: Profile): void {
    if (profile.active) return;

    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete profile "${profile.name}" (#${profile.profileNo})? All alarms in this profile will be lost.`,
        title: 'Delete Profile',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.profileService.delete(profile.profileNo).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete profile', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('Profile deleted', 'OK', { duration: 3000 });
            this.loadProfiles();
          },
        });
      }
    });
  }

  duplicateProfile(profile: Profile): void {
    const ref = this.dialog.open(ProfileCopyDialogComponent, {
      width: '400px',
      data: profile,
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadProfiles();
    });
  }

  editProfile(profile: Profile): void {
    const ref = this.dialog.open(ProfileEditDialogComponent, {
      width: '400px',
      data: profile,
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadProfiles();
    });
  }

  loadProfiles(): void {
    this.loading.set(true);
    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load profiles', 'OK', { duration: 3000 });
        },
        next: profiles => {
          this.profiles.set(profiles);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadProfiles();
  }

  openAddDialog(): void {
    const ref = this.dialog.open(ProfileAddDialogComponent, {
      width: '400px',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadProfiles();
    });
  }

  switchProfile(profile: Profile): void {
    this.switching.set(true);
    this.profileService
      .switchProfile(profile.profileNo)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.switching.set(false);
          this.snackBar.open('Failed to switch profile', 'OK', { duration: 3000 });
        },
        next: res => {
          this.switching.set(false);
          // Save the new JWT with updated profileNo
          if (res.token) {
            this.authService.setToken(res.token);
          }
          this.snackBar.open(`Switched to profile "${profile.name}"`, 'OK', {
            duration: 3000,
          });
          // Reload auth state and profiles
          this.authService.loadCurrentUser();
          this.loadProfiles();
        },
      });
  }
}
