import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from '../../../shared/components/geofence-approval-dialog/geofence-approval-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
  ],
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

  readonly activeFilter = signal<string>('all');
  readonly allGeofences = signal<UserGeofence[]>([]);
  readonly filteredGeofences = computed(() => {
    const filter = this.activeFilter();
    const all = this.allGeofences();
    if (filter === 'all') return all;
    return all.filter(g => g.status === filter);
  });

  readonly loading = signal(true);

  readonly statusCounts = computed(() => {
    const all = this.allGeofences();
    return {
      active: all.filter(g => g.status === 'active').length,
      all: all.length,
      approved: all.filter(g => g.status === 'approved').length,
      pending_review: all.filter(g => g.status === 'pending_review').length,
      rejected: all.filter(g => g.status === 'rejected').length,
    };
  });

  async adminDelete(geofence: UserGeofence): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Permanently delete "${geofence.displayName}" (owned by ${geofence.humanId})? This will remove it from the user's areas and clean up all associated data.`,
        title: 'Admin Delete Geofence',
        warn: true,
      } as ConfirmDialogData,
    });
    const confirmed = await firstValueFrom(ref.afterClosed());
    if (confirmed) {
      this.adminGeofenceService
        .adminDelete(geofence.id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => this.snackBar.open('Failed to delete geofence', 'OK', { duration: 3000 }),
          next: () => {
            this.allGeofences.update(list => list.filter(g => g.id !== geofence.id));
            this.snackBar.open(`"${geofence.displayName}" deleted`, 'OK', { duration: 3000 });
          },
        });
    }
  }

  ngOnInit(): void {
    this.loadAll();
  }

  openReviewDialog(geofence: UserGeofence): void {
    const ref = this.dialog.open(GeofenceApprovalDialogComponent, {
      width: '480px',
      data: { geofence } as GeofenceApprovalDialogData,
    });

    ref.afterClosed().subscribe((result: GeofenceApprovalDialogResult | null) => {
      if (!result) return;

      if (result.action === 'approve') {
        this.adminGeofenceService
          .approveSubmission(geofence.id, { promotedName: result.promotedName })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => this.snackBar.open('Failed to approve submission', 'OK', { duration: 3000 }),
            next: updated => {
              this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
              this.snackBar.open(`"${geofence.displayName}" approved`, 'OK', { duration: 3000 });
            },
          });
      } else {
        this.adminGeofenceService
          .rejectSubmission(geofence.id, { reviewNotes: result.reviewNotes! })
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => this.snackBar.open('Failed to reject submission', 'OK', { duration: 3000 }),
            next: updated => {
              this.allGeofences.update(list => list.map(g => (g.id === geofence.id ? updated : g)));
              this.snackBar.open(`"${geofence.displayName}" rejected`, 'OK', { duration: 3000 });
            },
          });
      }
    });
  }

  private loadAll(): void {
    this.loading.set(true);
    this.adminGeofenceService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load geofences', 'OK', { duration: 3000 });
        },
        next: geofences => {
          this.allGeofences.set(geofences);
          this.loading.set(false);
        },
      });
  }
}
