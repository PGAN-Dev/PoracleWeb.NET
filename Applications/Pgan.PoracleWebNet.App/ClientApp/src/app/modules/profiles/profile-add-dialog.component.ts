import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

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
  selector: 'app-profile-add-dialog',
  standalone: true,
  styleUrl: './profile-add-dialog.component.scss',
  templateUrl: './profile-add-dialog.component.html',
})
export class ProfileAddDialogComponent {
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<ProfileAddDialogComponent>);

  existingNames = signal<Set<string>>(new Set());
  nameError = signal('');
  profileName = '';
  readonly saving = signal(false);

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(profiles => {
        this.existingNames.set(new Set(profiles.map(p => (p.name ?? '').toLowerCase())));
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
    this.profileService.create({ name }).subscribe({
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to create profile', 'OK', { duration: 3000 });
      },
      next: profile => {
        this.saving.set(false);
        this.snackBar.open(`Profile "${profile.name}" created`, 'OK', { duration: 3000 });
        this.dialogRef.close(profile);
      },
    });
  }
}
