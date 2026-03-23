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

import { NestAddDialogComponent } from './nest-add-dialog.component';
import { NestEditDialogComponent } from './nest-edit-dialog.component';
import { Nest } from '../../core/models';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { NestService } from '../../core/services/nest.service';
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
  selector: 'app-nest-list',
  standalone: true,
  styleUrl: './nest-list.component.scss',
  templateUrl: './nest-list.component.html',
})
export class NestListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly nestService = inject(NestService);
  private readonly snackBar = inject(MatSnackBar);
  readonly loading = signal(true);
  readonly nests = signal<Nest[]>([]);
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
      for (const uid of ids) await firstValueFrom(this.nestService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadNests();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.nestService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadNests();
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
          message: 'Delete ALL nest alarms? This cannot be undone.',
          title: 'Delete All Nest Alarms',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.nestService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('All nest alarms deleted', 'OK', { duration: 3000 });
              this.loadNests();
            },
          });
      });
  }

  deleteNest(nest: Nest): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete',
          message: `Delete the alarm for ${this.getPokemonName(nest.pokemonId)}?`,
          title: 'Delete Nest Alarm',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.nestService.delete(nest.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('Nest alarm deleted', 'OK', { duration: 3000 });
              this.loadNests();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editNest(nest: Nest): void {
    this.dialog
      .open(NestEditDialogComponent, { width: '600px', data: nest, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadNests();
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getPokemonImage(pokemonId: number): string {
    return this.iconService.getPokemonUrl(pokemonId);
  }

  getPokemonName(id: number): string {
    return this.masterData.getPokemonName(id);
  }

  loadNests(): void {
    this.loading.set(true);
    this.nestService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: n => {
          this.nests.set(n);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadNests();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    this.dialog
      .open(NestAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadNests();
      });
  }

  selectAll(): void {
    const ids = new Set(this.nests().map(i => i.uid));
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
        this.nestService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadNests();
          },
        });
      }
    });
  }
}
