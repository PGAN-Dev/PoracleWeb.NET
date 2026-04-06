import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import { MaxBattleAddDialogComponent } from './max-battle-add-dialog.component';
import { MaxBattleEditDialogComponent, MaxBattleEditDialogData } from './max-battle-edit-dialog.component';
import { MaxBattle } from '../../core/models';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MaxBattleService } from '../../core/services/max-battle.service';
import { SettingsService } from '../../core/services/settings.service';
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
    AlarmInfoComponent,
  ],
  selector: 'app-max-battle-list',
  standalone: true,
  styleUrl: './max-battle-list.component.scss',
  templateUrl: './max-battle-list.component.html',
})
export class MaxBattleListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly maxBattleService = inject(MaxBattleService);
  private moves: Record<string, string> = {};
  private readonly settingsService = inject(SettingsService);

  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly maxBattles = signal<MaxBattle[]>([]);
  readonly selectedIds = signal(new Set<number>());
  readonly selectMode = signal(false);
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
      for (const uid of ids) {
        await firstValueFrom(this.maxBattleService.delete(uid));
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
      await firstValueFrom(this.maxBattleService.updateBulkDistance(ids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadData();
      this.snackBar.open(`Updated distance for ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete All',
        message: 'Are you sure you want to delete ALL Max Battle alarms? This action cannot be undone.',
        title: 'Delete All Max Battle Alarms',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.maxBattleService.deleteAll().subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('All Max Battle alarms deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deleteMaxBattle(maxBattle: MaxBattle): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete the alarm for ${this.getTitle(maxBattle)}?`,
        title: 'Delete Max Battle Alarm',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.maxBattleService.delete(maxBattle.uid).subscribe({
          error: () => {
            this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 });
          },
          next: () => {
            this.snackBar.open('Max Battle alarm deleted', 'OK', { duration: 3000 });
            this.loadData();
          },
        });
      }
    });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editMaxBattle(maxBattle: MaxBattle): void {
    const ref = this.dialog.open(MaxBattleEditDialogComponent, {
      width: '600px',
      data: { item: maxBattle } as MaxBattleEditDialogData,
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  getFormName(pokemonId: number, formId: number): string {
    return this.masterData.getFormName(pokemonId, formId);
  }

  getImage(maxBattle: MaxBattle): string {
    if (maxBattle.pokemonId && maxBattle.pokemonId !== 9000) {
      return this.iconService.getPokemonUrl(maxBattle.pokemonId);
    }
    return '';
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
        return '#d500f9';
    }
  }

  getLevelStars(level: number): number[] {
    if (level === 9000) return [];
    return Array.from({ length: level }, (_, i) => i);
  }

  getMoveName(moveId: number): string {
    return this.moves[String(moveId)] ?? `Move #${moveId}`;
  }

  getTitle(maxBattle: MaxBattle): string {
    if (maxBattle.pokemonId && maxBattle.pokemonId !== 9000) {
      return this.masterData.getPokemonName(maxBattle.pokemonId);
    }
    return 'Any Pokemon';
  }

  loadData(): void {
    this.loading.set(true);
    this.maxBattleService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: maxBattles => {
          this.maxBattles.set(maxBattles);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.settingsService
      .getConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(config => (this.moves = config.moves ?? {}));
    this.loadData();
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  openAddDialog(): void {
    const ref = this.dialog.open(MaxBattleAddDialogComponent, { width: '600px' });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadData();
    });
  }

  selectAll(): void {
    const ids = new Set<number>();
    this.maxBattles().forEach(m => ids.add(m.uid));
    this.selectedIds.set(ids);
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
        this.maxBattleService.updateAllDistance(distance).subscribe({
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
