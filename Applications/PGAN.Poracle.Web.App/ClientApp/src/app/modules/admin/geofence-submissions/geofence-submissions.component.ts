import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from '../../../shared/components/geofence-approval-dialog/geofence-approval-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButtonModule, MatCardModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, MatTooltipModule],
  selector: 'app-geofence-submissions',
  standalone: true,
  styleUrl: './geofence-submissions.component.scss',
  templateUrl: './geofence-submissions.component.html',
})
export class GeofenceSubmissionsComponent implements OnInit {
  private readonly adminGeofenceService = inject(AdminGeofenceService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly submissions = signal<UserGeofence[]>([]);

  ngOnInit(): void {
    this.loadSubmissions();
  }

  openReviewDialog(geofence: UserGeofence): void {
    const ref = this.dialog.open(GeofenceApprovalDialogComponent, {
      data: { geofence } as GeofenceApprovalDialogData,
      width: '480px',
    });

    ref.afterClosed().subscribe((result: GeofenceApprovalDialogResult | null) => {
      if (!result) return;

      if (result.action === 'approve') {
        this.adminGeofenceService
          .approveSubmission(geofence.id, { promotedName: result.promotedName })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => this.snackBar.open('Failed to approve submission', 'OK', { duration: 3000 }),
            next: () => {
              this.submissions.update(list => list.filter(g => g.id !== geofence.id));
              this.snackBar.open(`"${geofence.displayName}" approved`, 'OK', { duration: 3000 });
            },
          });
      } else {
        this.adminGeofenceService
          .rejectSubmission(geofence.id, { reviewNotes: result.reviewNotes! })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => this.snackBar.open('Failed to reject submission', 'OK', { duration: 3000 }),
            next: () => {
              this.submissions.update(list => list.filter(g => g.id !== geofence.id));
              this.snackBar.open(`"${geofence.displayName}" rejected`, 'OK', { duration: 3000 });
            },
          });
      }
    });
  }

  private loadSubmissions(): void {
    this.loading.set(true);
    this.adminGeofenceService
      .getSubmissions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load submissions', 'OK', { duration: 3000 });
        },
        next: submissions => {
          this.submissions.set(submissions);
          this.loading.set(false);
        },
      });
  }
}
