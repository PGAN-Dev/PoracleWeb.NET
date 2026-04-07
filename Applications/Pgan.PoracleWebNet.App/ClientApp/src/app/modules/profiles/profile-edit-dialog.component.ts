import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
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
    MatInputModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressBarModule,
  ],
  selector: 'app-profile-edit-dialog',
  standalone: true,
  styleUrl: './profile-edit-dialog.component.scss',
  templateUrl: './profile-edit-dialog.component.html',
})
export class ProfileEditDialogComponent {
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Profile>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ProfileEditDialogComponent>);

  existingNames = signal<Set<string>>(new Set());
  nameError = signal('');
  profileName = this.data.name;
  readonly saving = signal(false);

  constructor() {
    this.profileService.getAll().subscribe(profiles => {
      const names = profiles.filter(p => p.profileNo !== this.data.profileNo).map(p => (p.name ?? '').toLowerCase());
      this.existingNames.set(new Set(names));
    });
  }

  save(): void {
    const name = this.profileName.trim();
    if (!name) return;

    if (this.existingNames().has(name.toLowerCase())) {
      this.nameError.set('A profile with this name already exists');
      return;
    }
    this.nameError.set('');

    this.saving.set(true);
    this.profileService.update(this.data.profileNo, name).subscribe({
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to update profile', 'OK', { duration: 3000 });
      },
      next: profile => {
        this.saving.set(false);
        this.snackBar.open(`Profile renamed to "${profile.name}"`, 'OK', { duration: 3000 });
        this.dialogRef.close(profile);
      },
    });
  }
}
