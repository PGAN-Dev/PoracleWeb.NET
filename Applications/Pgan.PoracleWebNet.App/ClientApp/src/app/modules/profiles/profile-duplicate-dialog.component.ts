import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe } from '@ngx-translate/core';

import { Profile } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { ProfileService } from '../../core/services/profile.service';
import { serverReason } from '../../shared/utils/server-reason';

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
    TranslatePipe,
  ],
  selector: 'app-profile-duplicate-dialog',
  standalone: true,
  styleUrl: './profile-duplicate-dialog.component.scss',
  templateUrl: './profile-duplicate-dialog.component.html',
})
export class ProfileDuplicateDialogComponent {
  private readonly i18n = inject(I18nService);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Profile>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ProfileDuplicateDialogComponent>);
  readonly duplicating = signal(false);

  readonly existingNames = signal<Set<string>>(new Set());
  profileName = `${this.data.name} (copy)`;

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(profiles => {
        this.existingNames.set(new Set(profiles.map(p => (p.name ?? '').toLowerCase())));
      });
  }

  duplicate(): void {
    const name = this.profileName.trim();
    if (!name || this.hasNameConflict()) return;

    this.duplicating.set(true);
    this.profileService.duplicate(this.data.profileNo, name).subscribe({
      error: err => {
        this.duplicating.set(false);
        // The API says why it refused (a name with text-direction characters, say); show that.
        const reason = serverReason(err);
        this.snackBar.open(reason ?? this.i18n.instant('PROFILES.SNACK_FAILED_DUPLICATE'), this.i18n.instant('TOAST.OK'), {
          duration: reason ? 6000 : 3000,
        });
      },
      next: profile => {
        this.duplicating.set(false);
        this.snackBar.open(this.i18n.instant('PROFILES.SNACK_DUPLICATED_ALL'), this.i18n.instant('TOAST.OK'), {
          duration: 3000,
        });
        this.dialogRef.close(profile);
      },
    });
  }

  /** Checked as the user types so the button turns off before the click. */
  hasNameConflict(): boolean {
    const name = this.profileName.trim().toLowerCase();
    return name.length > 0 && this.existingNames().has(name);
  }
}
