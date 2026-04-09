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

import { LureAddDialogComponent } from './lure-add-dialog.component';
import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { Lure } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { LureService } from '../../core/services/lure.service';
import { TestAlertService } from '../../core/services/test-alert.service';
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
  selector: 'app-lure-list',
  standalone: true,
  styleUrl: './lure-list.component.scss',
  templateUrl: './lure-list.component.html',
})
export class LureListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  readonly loading = signal(true);
  readonly lures = signal<Lure[]>([]);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);
  readonly testAlertService = inject(TestAlertService);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
        message: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
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
      this.snackBar.open(this.i18n.instant('POKEMON.SNACK_BULK_DELETED', { count: ids.length }), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.lureService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadLures();
      this.snackBar.open(this.i18n.instant('POKEMON.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
          message: this.i18n.instant('POKEMON.CONFIRM_DELETE_ALL_MSG'),
          title: this.i18n.instant('LURES.PAGE_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.deleteAll().subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('LURES.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('LURES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  deleteLure(lure: Lure): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('COMMON.DELETE'),
          message: `${this.i18n.instant('COMMON.DELETE')} ${this.getLureName(lure.lureId)} ${this.i18n.instant('LURES.LURE_SUFFIX')}?`,
          title: this.i18n.instant('LURES.EDIT_DIALOG_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.lureService.delete(lure.uid).subscribe({
            error: () =>
              this.snackBar.open(this.i18n.instant('LURES.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
            next: () => {
              this.snackBar.open(this.i18n.instant('LURES.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
              this.loadLures();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
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

  getLureIcon(lureId: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/item/${lureId}.png`;
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

  selectAll(): void {
    const ids = new Set(this.lures().map(i => i.uid));
    this.selectedIds.set(ids);
  }

  sendTestAlert(lure: Lure): void {
    this.testAlertService.sendTestAlert('lure', lure.uid);
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
        this.lureService.updateAllDistance(distance).subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_FAILED_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_ALL_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadLures();
          },
        });
      }
    });
  }
}
