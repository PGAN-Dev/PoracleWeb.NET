import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule } from '@ngx-translate/core';

import { Profile } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
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
    TranslateModule,
  ],
  selector: 'app-profile-edit-dialog',
  standalone: true,
  styleUrl: './profile-edit-dialog.component.scss',
  templateUrl: './profile-edit-dialog.component.html',
})
export class ProfileEditDialogComponent {
  private readonly i18n = inject(I18nService);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Profile>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ProfileEditDialogComponent>);

  existingNames = signal<Set<string>>(new Set());
  nameError = signal('');
  profileName = this.data.name;
  readonly saving = signal(false);

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(profiles => {
        const names = profiles.filter(p => p.profileNo !== this.data.profileNo).map(p => (p.name ?? '').toLowerCase());
        this.existingNames.set(new Set(names));
      });
  }

  save(): void {
    const name = this.profileName.trim();
    if (!name) return;

    if (this.existingNames().has(name.toLowerCase())) {
      this.nameError.set(this.i18n.instant('DIALOG.PROMPT_CONFLICT'));
      return;
    }
    this.nameError.set('');

    this.saving.set(true);
    this.profileService.update(this.data.profileNo, name).subscribe({
      error: () => {
        this.saving.set(false);
        this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_UPDATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      },
      next: profile => {
        this.saving.set(false);
        this.snackBar.open(this.i18n.instant('PROFILES.SNACK_UPDATED', { name: profile.name }), this.i18n.instant('TOAST.OK'), {
          duration: 3000,
        });
        this.dialogRef.close(profile);
      },
    });
  }
}
