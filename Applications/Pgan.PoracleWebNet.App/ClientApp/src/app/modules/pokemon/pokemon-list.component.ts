import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PokemonAddDialogComponent } from './pokemon-add-dialog.component';
import { PokemonEditDialogComponent } from './pokemon-edit-dialog.component';
import { Monster } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslateModule,
  ],
  selector: 'app-pokemon-list',
  standalone: true,
  styleUrl: './pokemon-list.component.scss',
  templateUrl: './pokemon-list.component.html',
})
export class PokemonListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  // Search & quick filters
  readonly searchControl = new FormControl('');
  private readonly searchValue = toSignal(this.searchControl.valueChanges, { initialValue: '' });

  private readonly snackBar = inject(MatSnackBar);
  readonly activeFilter = signal<string | null>(null);
  readonly activeGen = signal<{ label: string; min: number; max: number } | null>(null);

  readonly monsters = signal<Monster[]>([]);
  readonly sortBy = signal<'name' | 'id' | 'evolution' | 'generation'>('name');
  readonly filteredMonsters = computed(() => {
    const gen = this.activeGen();
    const sort = this.sortBy();
    const search = (this.searchValue() ?? '').toLowerCase();
    const filter = this.activeFilter();
    let list = this.monsters();

    // Filter by generation
    if (gen) {
      list = list.filter(m => {
        if (m.pokemonId === 0) return true;
        return m.pokemonId >= gen.min && m.pokemonId <= gen.max;
      });
    }

    // Search filter
    if (search) {
      list = list.filter(m => {
        const name = this.getPokemonName(m.pokemonId).toLowerCase();
        const id = String(m.pokemonId);
        return name.includes(search) || id.includes(search);
      });
    }

    // Quick filters
    if (filter === 'highiv') {
      list = list.filter(m => m.minIv >= 90);
    } else if (filter === 'pvp') {
      list = list.filter(m => m.pvpRankingLeague > 0);
    } else if (filter === 'distance') {
      list = list.filter(m => m.distance > 0);
    }

    // Separate "All Pokemon" (id=0) alarms to keep at the top
    const allPokemon = list.filter(m => m.pokemonId === 0);
    const specific = list.filter(m => m.pokemonId !== 0);

    // Sort specific alarms
    specific.sort((a, b) => {
      if (sort === 'name') {
        return this.getPokemonName(a.pokemonId).localeCompare(this.getPokemonName(b.pokemonId));
      } else if (sort === 'id') {
        return a.pokemonId - b.pokemonId;
      } else if (sort === 'generation') {
        // Sort by generation (dex number range), then by name within each gen
        const genA = this.getGenNumber(a.pokemonId);
        const genB = this.getGenNumber(b.pokemonId);
        if (genA !== genB) return genA - genB;
        return this.getPokemonName(a.pokemonId).localeCompare(this.getPokemonName(b.pokemonId));
      } else {
        // Evolution line: sort by base evolution Pokemon ID
        const baseA = this.getBaseEvolution(a.pokemonId);
        const baseB = this.getBaseEvolution(b.pokemonId);
        if (baseA !== baseB) return baseA - baseB;
        return a.pokemonId - b.pokemonId; // Within same line, sort by dex number
      }
    });

    return [...allPokemon, ...specific];
  });

  readonly generations = [
    { color: '#4CAF50', label: '1', max: 151, min: 1 },
    { color: '#FFD600', label: '2', max: 251, min: 152 },
    { color: '#2196F3', label: '3', max: 386, min: 252 },
    { color: '#9C27B0', label: '4', max: 493, min: 387 },
    { color: '#FF5722', label: '5', max: 649, min: 494 },
    { color: '#E91E63', label: '6', max: 721, min: 650 },
    { color: '#00BCD4', label: '7', max: 809, min: 722 },
    { color: '#795548', label: '8', max: 905, min: 810 },
    { color: '#607D8B', label: '9', max: 1025, min: 906 },
  ];

  readonly loading = signal(true);

  readonly selectedIds = signal(new Set<number>());

  // Bulk operations
  readonly selectMode = signal(false);

  readonly skeletonCards = Array.from({ length: 8 });
  readonly testAlertService = inject(TestAlertService);

  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_MSG', { count: this.selectedIds().size }),
        title: this.i18n.instant('POKEMON.CONFIRM_BULK_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = [...this.selectedIds()];
      for (const uid of ids) {
        await firstValueFrom(this.monsterService.delete(uid));
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadMonsters();
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
      await firstValueFrom(this.monsterService.updateBulkDistance(uids, distance));
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadMonsters();
      this.snackBar.open(this.i18n.instant('POKEMON.SNACK_BULK_DISTANCE', { count: uids.length }), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  deleteAll(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('POKEMON.CONFIRM_DELETE_ALL_MSG'),
        title: this.i18n.instant('POKEMON.CONFIRM_DELETE_ALL_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.monsterService.deleteAll().subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
              duration: 3000,
            });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_DELETED_ALL'), this.i18n.instant('COMMON.OK'), {
              duration: 3000,
            });
            this.loadMonsters();
          },
        });
      }
    });
  }

  deleteMonster(monster: Monster): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('POKEMON.CONFIRM_DELETE_MSG', { name: this.getPokemonName(monster.pokemonId) }),
        title: this.i18n.instant('POKEMON.CONFIRM_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.monsterService.delete(monster.uid).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_FAILED_DELETE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_DELETED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadMonsters();
          },
        });
      }
    });
  }

  deselectAll(): void {
    this.selectedIds.set(new Set());
  }

  editMonster(monster: Monster): void {
    const ref = this.dialog.open(PokemonEditDialogComponent, {
      width: '600px',
      data: monster,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadMonsters();
    });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getGenderDisplay(gender: number): string {
    switch (gender) {
      case 1:
        return '\u2642 Male';
      case 2:
        return '\u2640 Female';
      case 3:
        return 'Genderless';
      default:
        return 'All';
    }
  }

  getIvDisplay(minIv: number, maxIv: number): string {
    if (minIv === -1) return 'No IV Filter';
    return `${minIv}-${maxIv}%`;
  }

  getLeagueName(league: number): string {
    switch (league) {
      case 500:
        return 'Little';
      case 1500:
        return 'Great';
      case 2500:
        return 'Ultra';
      default:
        return `${league}`;
    }
  }

  getPokemonImage(pokemonId: number, form: number): string {
    return this.iconService.getPokemonUrl(pokemonId, form);
  }

  getPokemonName(id: number): string {
    if (id === 0) return this.i18n.instant('POKEMON.ALL_POKEMON');
    return this.masterData.getPokemonName(id);
  }

  getSizeLabel(value: number): string {
    switch (value) {
      case 1:
        return 'XXS';
      case 2:
        return 'XS';
      case 3:
        return 'Normal';
      case 4:
        return 'XL';
      case 5:
        return 'XXL';
      default:
        return 'Any';
    }
  }

  getSizePillText(monster: Monster): string {
    if (monster.size === monster.maxSize) {
      return this.getSizeLabel(monster.size);
    }
    return `${this.getSizeLabel(monster.size)}-${this.getSizeLabel(monster.maxSize)}`;
  }

  getTypeColor(pokemonId: number): string {
    // Color by generation range
    if (pokemonId === 0) return '#9E9E9E';
    if (pokemonId <= 151) return '#4CAF50'; // Gen 1
    if (pokemonId <= 251) return '#FFD600'; // Gen 2
    if (pokemonId <= 386) return '#2196F3'; // Gen 3
    if (pokemonId <= 493) return '#9C27B0'; // Gen 4
    if (pokemonId <= 649) return '#FF5722'; // Gen 5
    if (pokemonId <= 721) return '#E91E63'; // Gen 6
    if (pokemonId <= 809) return '#00BCD4'; // Gen 7
    if (pokemonId <= 905) return '#795548'; // Gen 8
    return '#607D8B'; // Gen 9+
  }

  loadMonsters(): void {
    this.loading.set(true);
    this.monsterService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: monsters => {
          this.monsters.set(monsters);
          this.loading.set(false);
        },
      });
  }

  ngOnInit(): void {
    // Ensure masterdata is loaded
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadMonsters();
  }

  onImageError(event: Event, pokemonId: number): void {
    const img = event.target as HTMLImageElement;
    const fallback = this.iconService.getPokemonFallbackUrl(pokemonId);
    if (!img.src.endsWith(`/${pokemonId}.png`)) {
      img.src = fallback;
    } else {
      img.style.display = 'none';
    }
  }

  openAddDialog(): void {
    const ref = this.dialog.open(PokemonAddDialogComponent, {
      width: '600px',
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadMonsters();
    });
  }

  selectAll(): void {
    const ids = new Set(this.filteredMonsters().map(m => m.uid));
    this.selectedIds.set(ids);
  }

  sendTestAlert(monster: Monster): void {
    this.testAlertService.sendTestAlert('pokemon', monster.uid);
  }

  toggleFilter(filter: string): void {
    this.activeFilter.set(this.activeFilter() === filter ? null : filter);
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

  // Bulk operations
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
        this.monsterService.updateAllDistance(distance).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_FAILED_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('POKEMON.SNACK_ALL_DISTANCE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
            this.loadMonsters();
          },
        });
      }
    });
  }

  private getBaseEvolution(id: number): number {
    return this.masterData.getBaseEvolution(id);
  }

  private getGenNumber(id: number): number {
    for (const gen of this.generations) {
      if (id >= gen.min && id <= gen.max) return +gen.label;
    }
    return 10;
  }
}
