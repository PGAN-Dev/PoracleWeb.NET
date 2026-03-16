import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ProfileService } from '../../core/services/profile.service';

@Component({
  selector: 'app-profile-add-dialog',
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
    <h2 mat-dialog-title>Create Profile</h2>
    @if (saving()) {
      <mat-progress-bar mode="indeterminate"></mat-progress-bar>
    }
    <mat-dialog-content>
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
        <mat-icon>add</mat-icon> Create
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .full-width {
        width: 100%;
      }
    `,
  ],
})
export class ProfileAddDialogComponent {
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<ProfileAddDialogComponent>);

  profileName = '';
  readonly saving = signal(false);

  save(): void {
    const name = this.profileName.trim();
    if (!name) return;

    this.saving.set(true);
    this.profileService.create({ name }).subscribe({
      next: (profile) => {
        this.saving.set(false);
        this.snackBar.open(`Profile "${profile.name}" created`, 'OK', { duration: 3000 });
        this.dialogRef.close(profile);
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open('Failed to create profile', 'OK', { duration: 3000 });
      },
    });
  }
}
