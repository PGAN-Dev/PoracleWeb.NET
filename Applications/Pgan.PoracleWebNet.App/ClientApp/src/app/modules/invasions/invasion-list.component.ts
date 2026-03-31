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

import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import { EVENT_TYPE_INFO, getDisplayName as displayName, getGruntIconUrl, isEventType as checkEventType } from './invasion.constants';
import { Invasion } from '../../core/models';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
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
  selector: 'app-invasion-list',
  standalone: true,
  styleUrl: './invasion-list.component.scss',
  templateUrl: './invasion-list.component.html',
})
export class InvasionListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  readonly invasions = signal<Invasion[]>([]);
  readonly loading = signal(true);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);

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
      for (const uid of ids) await firstValueFrom(this.invasionService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.invasionService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadInvasions();
      this.snackBar.open(`Updated distance for ${uids.length} alarms`, 'OK', {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete All',
          message: 'Delete ALL invasion alarms? This cannot be undone.',
          title: 'Delete All Invasion Alarms',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('All invasion alarms deleted', 'OK', { duration: 3000 });
              this.loadInvasions();
            },
          });
      });
  }

  deleteInvasion(invasion: Invasion): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete',
          message: `Delete alarm for ${invasion.gruntType || 'this grunt'}?`,
          title: 'Delete Invasion Alarm',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.invasionService.delete(invasion.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('Invasion alarm deleted', 'OK', { duration: 3000 });
              this.loadInvasions();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editInvasion(invasion: Invasion): void {
    this.dialog
      .open(InvasionEditDialogComponent, { width: '600px', data: invasion, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getCardAccent(gruntType: string | null): string | null {
    const info = EVENT_TYPE_INFO[gruntType ?? ''];
    return info ? info.color : null;
  }

  getDisplayName(gruntType: string | null): string {
    return displayName(gruntType);
  }

  getEventColor(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.color ?? '';
  }

  getEventIcon(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(gruntType: string | null): string {
    return EVENT_TYPE_INFO[gruntType ?? '']?.imgUrl ?? '';
  }

  getGruntIcon(gruntType: string | null): string {
    return getGruntIconUrl(gruntType);
  }

  isEventType(gruntType: string | null): boolean {
    return checkEventType(gruntType);
  }

  loadInvasions(): void {
    this.loading.set(true);
    this.invasionService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: inv => {
          this.invasions.set(inv);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadInvasions();
  }

  openAddDialog(): void {
    this.dialog
      .open(InvasionAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadInvasions();
      });
  }

  selectAll(): void {
    const ids = new Set(this.invasions().map(i => i.uid));
    this.selectedIds.set(ids);
  }

  toggleSelect(uid: number): void {
    const current = new Set(this.selectedIds());
    current.has(uid) ? current.delete(uid) : current.add(uid);
    this.selectedIds.set(current);
  }

  toggleSelectMode(): void {
    this.selectMode.update(v => !v);
    if (!this.selectMode()) this.selectedIds.set(new Set());
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        this.invasionService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadInvasions();
          },
        });
      }
    });
  }
}
