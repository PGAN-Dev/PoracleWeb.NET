import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Profile } from '../../core/models';
import { ProfileService } from '../../core/services/profile.service';

@Component({
  selector: 'app-profile-edit-dialog',
  standalone: true,
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
  template: `
    <h2 mat-dialog-title>Edit Profile</h2>
    @if (saving()) {
      <mat-progress-bar mode="indeterminate"></mat-progress-bar>
    }
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Profile Number</mat-label>
        <input matInput [value]="data.profileNo" disabled />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Profile Name</mat-label>
        <input
          matInput
          [(ngModel)]="profileName"
          placeholder="Enter profile name"
          maxlength="32"
          (keyup.enter)="save()"
        />
        <mat-hint>{{ profileName.length }}/32</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button
        mat-raised-button
        color="primary"
        (click)="save()"
        [disabled]="saving() || !profileName.trim()"
      >
        <mat-icon>save</mat-icon> Save
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .full-width {
        width: 100%;
        margin-bottom: 8px;
      }
    `,
  ],
})
export class ProfileEditDialogComponent {
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<Profile>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ProfileEditDialogComponent>);

  profileName = this.data.name;
  readonly saving = signal(false);

  save(): void {
    const name = this.profileName.trim();
    if (!name) return;

    this.saving.set(true);
    this.profileService.update(this.data.profileNo, name).subscribe({
      next: (profile) => {
        this.saving.set(false);
        this.snackBar.open(`Profile renamed to "${profile.name}"`, 'OK', { duration: 3000 });
        this.dialogRef.close(profile);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to update profile', 'OK', { duration: 3000 });
      },
    });
  }
}
