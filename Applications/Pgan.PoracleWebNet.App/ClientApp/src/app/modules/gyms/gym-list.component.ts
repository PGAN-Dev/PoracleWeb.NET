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
import { firstValueFrom, forkJoin } from 'rxjs';

import { GymAddDialogComponent } from './gym-add-dialog.component';
import { GymEditDialogComponent } from './gym-edit-dialog.component';
import { Gym } from '../../core/models';
import { GymService } from '../../core/services/gym.service';
import { ScannerService } from '../../core/services/scanner.service';
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
  selector: 'app-gym-list',
  standalone: true,
  styleUrl: './gym-list.component.scss',
  templateUrl: './gym-list.component.html',
})
export class GymListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly gymService = inject(GymService);
  private readonly scannerService = inject(ScannerService);
  private readonly snackBar = inject(MatSnackBar);
  readonly gymNames = signal<Record<string, string>>({});
  readonly gyms = signal<Gym[]>([]);
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
      for (const uid of ids) await firstValueFrom(this.gymService.delete(uid));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadGyms();
      this.snackBar.open(`Deleted ${ids.length} alarms`, 'OK', { duration: 3000 });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px' });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = [...this.selectedIds()];
      await firstValueFrom(this.gymService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadGyms();
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
          message: 'Delete ALL gym alarms? This cannot be undone.',
          title: 'Delete All Gym Alarms',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.gymService.deleteAll().subscribe({
            error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('All gym alarms deleted', 'OK', { duration: 3000 });
              this.loadGyms();
            },
          });
      });
  }

  deleteGym(gym: Gym): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: 'Delete',
          message: `Delete the ${this.getTeamName(gym.team)} gym alarm?`,
          title: 'Delete Gym Alarm',
          warn: true,
        } as ConfirmDialogData,
      })
      .afterClosed()
      .subscribe(c => {
        if (c)
          this.gymService.delete(gym.uid).subscribe({
            error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }),
            next: () => {
              this.snackBar.open('Gym alarm deleted', 'OK', { duration: 3000 });
              this.loadGyms();
            },
          });
      });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editGym(gym: Gym): void {
    this.dialog
      .open(GymEditDialogComponent, { width: '600px', data: gym, maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadGyms();
      });
  }

  formatDistance(meters: number): string {
    return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${meters} m`;
  }

  getGymIcon(team: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/gym/${team}.png`;
  }

  getTeamColor(team: number): string {
    switch (team) {
      case 0:
        return '#9E9E9E';
      case 1:
        return '#2196F3';
      case 2:
        return '#F44336';
      case 3:
        return '#FFC107';
      default:
        return '#9E9E9E';
    }
  }

  getTeamName(team: number): string {
    switch (team) {
      case 0:
        return 'Neutral';
      case 1:
        return 'Mystic (Blue)';
      case 2:
        return 'Valor (Red)';
      case 3:
        return 'Instinct (Yellow)';
      default:
        return `Team ${team}`;
    }
  }

  loadGyms(): void {
    this.loading.set(true);
    this.gymService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: g => {
          this.gyms.set(g);
          this.loading.set(false);
          this.resolveGymNames(g);
        },
      });
  }

  ngOnInit(): void {
    this.loadGyms();
  }

  openAddDialog(): void {
    this.dialog
      .open(GymAddDialogComponent, { width: '600px', maxHeight: '90vh' })
      .afterClosed()
      .subscribe(r => {
        if (r) this.loadGyms();
      });
  }

  selectAll(): void {
    const ids = new Set(this.gyms().map(i => i.uid));
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
        this.gymService.updateAllDistance(distance).subscribe({
          error: () => this.snackBar.open('Failed to update distances', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('All distances updated', 'OK', { duration: 3000 });
            this.loadGyms();
          },
        });
      }
    });
  }

  private resolveGymNames(gyms: Gym[]): void {
    const ids = [...new Set(gyms.filter(g => g.gymId).map(g => g.gymId!))];
    if (ids.length === 0) return;
    const lookups = Object.fromEntries(ids.map(id => [id, this.scannerService.getGymById(id)]));
    forkJoin(lookups).subscribe(results => {
      const names: Record<string, string> = {};
      for (const [id, result] of Object.entries(results)) {
        if (result?.name) names[id] = result.name;
      }
      this.gymNames.set(names);
    });
  }
}
