import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom, forkJoin } from 'rxjs';

import { RaidAddDialogComponent } from './raid-add-dialog.component';
import { RaidEditDialogComponent, RaidEditDialogData } from './raid-edit-dialog.component';
import { Raid, Egg } from '../../core/models';
import { EggService } from '../../core/services/egg.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidService } from '../../core/services/raid.service';
import { AlarmInfoComponent } from '../../shared/components/alarm-info/alarm-info.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatTabsModule,
    AlarmInfoComponent,
  ],
  selector: 'app-raid-list',
  standalone: true,
  styleUrl: './raid-list.component.scss',
  templateUrl: './raid-list.component.html',
})
export class RaidListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly eggService = inject(EggService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly raidService = inject(RaidService);
  private readonly snackBar = inject(MatSnackBar);

  readonly eggs = signal<Egg[]>([]);
  readonly loading = signal(true);
  readonly raids = signal<Raid[]>([]);
  readonly selectMode = signal(false);
  readonly selectedIds = signal(new Set<number>());
  readonly skeletonCards = Array.from({ length: 6 });

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete Selected',
        message: `Are you sure you want to delete ${this.selectedIds().size} alarms?`,
        title: 'Delete Selected Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      const raidUids = new Set(this.raids().map(r => r.uid));
      for (const uid of ids) {
        if (raidUids.has(uid)) {
          await firstValueFrom(this.raidService.delete(uid));
        } else {
          await firstValueFrom(this.eggService.delete(uid));
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const ids = [...this.selectedIds()];
      const raidUids = this.raids().map(r => r.uid);
      const selectedRaidUids = ids.filter(id => raidUids.includes(id));
      const selectedEggUids = ids.filter(id => !raidUids.includes(id));
      if (selectedRaidUids.length > 0)
        await firstValueFrom(this.raidService.updateBulkDistance(selectedRaidUids, distance));
      if (selectedEggUids.length > 0)
        await firstValueFrom(this.eggService.updateBulkDistance(selectedEggUids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(`Updated distance for ${ids.length} alarms`, 'OK', {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete All',
        message: 'Are you sure you want to delete ALL raid and egg alarms? This action cannot be undone.',
        title: 'Delete All Raid & Egg Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        forkJoin([this.raidService.deleteAll(), this.eggService.deleteAll()]).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('All raid & egg alarms deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteEgg(egg: Egg): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete the Level ${egg.level} egg alarm?`,
        title: 'Delete Egg Alarm',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.eggService.delete(egg.uid).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('Egg alarm deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteRaid(raid: Raid): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete the alarm for ${this.getRaidTitle(raid)}?`,
        title: 'Delete Raid Alarm',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.raidService.delete(raid.uid).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('Raid alarm deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  editEgg(egg: Egg): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      data: { item: egg, type: 'egg' } as RaidEditDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  editRaid(raid: Raid): void {
    const ref = this.dialog.open(RaidEditDialogComponent, {
      width: '600px',
      data: { item: raid, type: 'raid' } as RaidEditDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getEggImage(level: number): string {
    return this.iconService.getRaidEggUrl(level);
  }

  getLevelColor(level: number): string {
    switch (level) {
      case 1:
        return '#FF9800';
      case 2:
        return '#FF9800';
      case 3:
        return '#F44336';
      case 4:
        return '#F44336';
      case 5:
        return '#9C27B0';
      case 6:
        return '#4A148C';
      default:
        return '#9E9E9E';
    }
  }

  getLevelStars(level: number): number[] {
    return Array.from({ length: level }, (_, i) => i);
  }

  getRaidImage(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(raid.pokemonId);
    }
    return this.iconService.getRaidEggUrl(raid.level);
  }

  getRaidTitle(raid: Raid): string {
    if (raid.pokemonId && raid.pokemonId !== 9000) {
      return this.masterData.getPokemonName(raid.pokemonId);
    }
    return `Level ${raid.level} Raid`;
  }

  getGymIcon(team: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/gym/${team}.png`;
  }

  getTeamColor(team: number): string {
    switch (team) {
      case 1:
        return '#2196F3';
      case 2:
        return '#F44336';
      case 3:
        return '#FFEB3B';
      default:
        return 'inherit';
    }
  }

  getTeamName(team: number): string {
    switch (team) {
      case 1:
        return 'Mystic';
      case 2:
        return 'Valor';
      case 3:
        return 'Instinct';
      default:
        return 'Any';
    }
  }

  loadData(): void {
    this.loading.set(true);
    forkJoin([this.raidService.getAll(), this.eggService.getAll()])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: ([raids, eggs]) => {
          this.raids.set(raids);
          this.eggs.set(eggs);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadData();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(RaidAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  selectAll(): void {
    const ids = new Set<number>();
    this.raids().forEach(r => ids.add(r.uid));
    this.eggs().forEach(e => ids.add(e.uid));
    this.selectedIds.set(ids);
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  toggleSelect(uid: number): void {
    const current = new Set(this.selectedIds());
    if (current.has(uid)) {
      current.delete(uid);
    } else {
      current.add(uid);
    }
    this.selectedIds.set(current);
  }

  toggleSelectMode(): void {
    this.selectMode.update(v => !v);
    if (!this.selectMode()) {
      this.selectedIds.set(new Set());
    }
  }

  updateAllDistance(): void {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    ref.afterClosed().subscribe(distance => {
      if (distance !== null && distance !== undefined) {
        forkJoin([this.raidService.updateAllDistance(distance), this.eggService.updateAllDistance(distance)]).subscribe({
          error: () => {
            this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }
}
