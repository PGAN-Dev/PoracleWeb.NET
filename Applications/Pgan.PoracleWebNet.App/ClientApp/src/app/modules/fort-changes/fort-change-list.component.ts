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
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { FortChangeAddDialogComponent } from './fort-change-add-dialog.component';
import { FortChangeEditDialogComponent } from './fort-change-edit-dialog.component';
import { FortChange } from '../../core/models';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
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
    TranslateModule,
  ],
  selector: 'app-fort-change-list',
  standalone: true,
  styleUrl: './fort-change-list.component.scss',
  templateUrl: './fort-change-list.component.html',
})
export class FortChangeListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly fortChanges = signal<FortChange[]>([]);
  readonly loading = signal(true);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('ALARM.SELECTED_COUNT', { count: this.selectedIds().size }),
        title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) await firstValueFrom(this.fortChangeService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadItems();
      this.snackBar.open(`Deleted ${ids.length} alarms`, this.i18n.instant('COMMON.OK'), { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.fortChangeService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadItems();
      this.snackBar.open(`Updated distance for ${uids.length} alarms`, this.i18n.instant('COMMON.OK'), { duration: 3000 });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: 'Delete ALL fort change alarms? This cannot be undone.',
          title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.fortChangeService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open('All fort change alarms deleted', this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadItems();
            },
          });
      });
  }

  deleteItem(item: FortChange): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: `Delete the ${this.formatFortType(item.fortType)} fort change alarm?`,
          title: this.i18n.instant('FORT_CHANGES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.fortChangeService.delete(item.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open('Fort change alarm deleted', this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadItems();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editItem(item: FortChange): void {
    this.dialog
      .open(FortChangeEditDialogComponent, { width: '600px', data: item, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  formatChangeTypes(types: string[]): string {
    if (!types || types.length === 0) return 'All changes';
    return types
      .map(t => {
        switch (t) {
          case 'name':
            return 'Name';
          case 'location':
            return 'Location';
          case 'image_url':
            return 'Image';
          case 'removal':
            return 'Removal';
          case 'new':
            return 'New';
          default:
            return t;
        }
      })
      .join(', ');
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  formatFortType(type: string | null): string {
    switch (type) {
      case 'pokestop':
        return 'Pokestop';
      case 'gym':
        return 'Gym';
      default:
        return 'Everything';
    }
  }

  loadItems(): void {
    this.loading.set(true);
    this.fortChangeService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: items => {
          this.fortChanges.set(items);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.loadItems();
  }

  openAddDialog(): void {
    this.dialog
      .open(FortChangeAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadItems();
      });
  }

  selectAll(): void {
    const ids = new Set(this.fortChanges().map(i => i.uid));
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
        this.fortChangeService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', this.i18n.instant('COMMON.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadItems();
          },
        });
      }
    });
  }
}
