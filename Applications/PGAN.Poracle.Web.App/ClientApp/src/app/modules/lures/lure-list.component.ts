import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { LureAddDialogComponent } from './lure-add-dialog.component';
import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { Lure } from '../../core/models';
import { LureService } from '../../core/services/lure.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  selector: 'app-lure-list',
  standalone: true,
  styleUrl: './lure-list.component.scss',
  templateUrl: './lure-list.component.html',
})
export class LureListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  readonly loading = signal(true);
  readonly lures = signal<Lure[]>([]);
  readonly selectMode = signal(false);
  readonly selectedIds = signal(new Set<number>());

  toggleSelectMode(): void {
    this.selectMode.update(v => !v);
    if (!this.selectMode()) this.selectedIds.set(new Set());
  }

  toggleSelect(uid: number): void {
    const current = new Set(this.selectedIds());
    current.has(uid) ? current.delete(uid) : current.add(uid);
    this.selectedIds.set(current);
  }

  selectAll(): void {
    const ids = new Set(this.lures().map(i => i.uid));
    this.selectedIds.set(ids);
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete Selected',
        message: `Delete ${this.selectedIds().size} alarms?`,
        title: 'Delete Selected Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) await firstValueFrom(this.lureService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadLures();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) await firstValueFrom(this.lureService.update(uid, { distance }));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadLures();
      this.snackBar.open(`Updated distance for ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete All',
          message: 'Delete ALL lure alarms? This cannot be undone.',
          title: 'Delete All Lure Alarms',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('All lure alarms deleted', 'OK', { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  deleteLure(lure: Lure): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete',
          message: `Delete the ${this.getLureName(lure.lureId)} lure alarm?`,
          title: 'Delete Lure Alarm',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.delete(lure.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('Lure alarm deleted', 'OK', { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  editLure(lure: Lure): void {
    this.dialog
      .open(LureEditDialogComponent, { width: '600px', data: lure, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadLures();
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getLureIcon(lureId: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/item/${lureId}.png`;
  }

  getLureColor(id: number): string {
    switch (id) {
      case 501:
        return '#FF9800';
      case 502:
        return '#03A9F4';
      case 503:
        return '#4CAF50';
      case 504:
        return '#9E9E9E';
      case 505:
        return '#2196F3';
      case 506:
        return '#FFC107';
      default:
        return '#9E9E9E';
    }
  }

  getLureName(id: number): string {
    switch (id) {
      case 501:
        return 'Normal';
      case 502:
        return 'Glacial';
      case 503:
        return 'Mossy';
      case 504:
        return 'Magnetic';
      case 505:
        return 'Rainy';
      case 506:
        return 'Golden';
      default:
        return `Lure #${id}`;
    }
  }

  loadLures(): void {
    this.loading.set(true);
    this.lureService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: l => {
          this.lures.set(l);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadLures();
  }

  openAddDialog(): void {
    this.dialog
      .open(LureAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadLures();
      });
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        this.lureService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadLures();
          },
        });
      }
    });
  }
}
