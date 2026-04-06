import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

import { Profile } from '../../core/models';
import { ProfileService } from '../../core/services/profile.service';

@Component({
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSnackBarModule,
  ],
  selector: 'app-profile-duplicate-dialog',
  standalone: true,
  styleUrl: './profile-duplicate-dialog.component.scss',
  templateUrl: './profile-duplicate-dialog.component.html',
})
export class ProfileDuplicateDialogComponent {
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Profile>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ProfileDuplicateDialogComponent>);
  readonly duplicating = signal(false);

  profileName = `${this.data.name} (copy)`;

  duplicate(): void {
    const name = this.profileName.trim();
    if (!name) return;

    this.duplicating.set(true);
    this.profileService.duplicate(this.data.profileNo, name).subscribe({
      error: () => {
        this.duplicating.set(false);
        this.snackBar.open('Failed to duplicate profile', 'OK', { duration: 3000 });
      },
      next: profile => {
        this.duplicating.set(false);
        this.snackBar.open(`Profile "${profile.name}" created with all alarms`, 'OK', { duration: 3000 });
        this.dialogRef.close(profile);
      },
    });
  }
}
