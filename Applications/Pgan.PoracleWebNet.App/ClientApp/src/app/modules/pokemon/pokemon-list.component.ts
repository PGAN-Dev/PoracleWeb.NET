import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PokemonAddDialogComponent } from './pokemon-add-dialog.component';
import { PokemonEditDialogComponent } from './pokemon-edit-dialog.component';
import { Monster } from '../../core/models';
import { AreaNameService } from '../../core/services/area-name.service';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { MonsterService } from '../../core/services/monster.service';
import { TestAlertService } from '../../core/services/test-alert.service';
import { AlarmCardComponent } from '../../shared/components/alarm-card/alarm-card.component';
import { AlarmEmptyStateComponent } from '../../shared/components/alarm-empty-state/alarm-empty-state.component';
import { AlarmListToolbarComponent, AlarmSortOption } from '../../shared/components/alarm-list-toolbar/alarm-list-toolbar.component';
import { BulkActionBarComponent } from '../../shared/components/bulk-action-bar/bulk-action-bar.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DistanceDialogComponent } from '../../shared/components/distance-dialog/distance-dialog.component';
import { QuietChipComponent } from '../../shared/components/quiet-chip/quiet-chip.component';
import { WhereChipComponent } from '../../shared/components/where-chip/where-chip.component';
import { WhereSheetComponent, WhereSheetData, whereSheetConfig } from '../../shared/components/where-sheet/where-sheet.component';
import { addOnArrival } from '../../shared/utils/add-on-arrival';
import { AddDialogData, isEditExisting, ruleSummary } from '../../shared/utils/add-outcome';
import { matchesSearch, visibleSelection } from '../../shared/utils/alarm-list-query';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { isAutoDelete as cleanIsAutoDelete, isEdit as cleanIsEdit } from '../../shared/utils/clean-flags';
import { readableChip } from '../../shared/utils/contrast';
import { NO_COSTUME } from '../../shared/utils/costumes';
import { distanceUpdateMessage, DistanceUpdateResult, skippedAny } from '../../shared/utils/distance-update';
import { minTimePillLabel } from '../../shared/utils/min-time';
import { hasLevelFilter } from '../../shared/utils/pokemon-level';
import { RuleEdge, speciesEdge } from '../../shared/utils/rule-colours';
import { serverReason } from '../../shared/utils/server-reason';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlarmEmptyStateComponent,
    AlarmCardComponent,
    AlarmListToolbarComponent,
    BulkActionBarComponent,
    QuietChipComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslatePipe,
    WhereChipComponent,
  ],
  selector: 'app-pokemon-list',
  standalone: true,
  styleUrl: './pokemon-list.component.scss',
  templateUrl: './pokemon-list.component.html',
})
export class PokemonListComponent implements OnInit {
  private readonly areaNames = inject(AreaNameService);
  private readonly areaService = inject(AreaService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  private readonly monsterService = inject(MonsterService);
  private readonly snackBar = inject(MatSnackBar);
  /** `?add=1` from the dashboard or onboarding opens the add dialog once. */
  private readonly takeAddRequest = addOnArrival();
  private readonly translate = inject(TranslateService);
  readonly activeFilter = signal<string | null>(null);

  readonly activeGen = signal<{ label: string; min: number; max: number } | null>(null);
  readonly monsters = signal<Monster[]>([]);
  // Search & quick filters
  readonly search = signal('');
  readonly sortBy = signal<string>('name');

  readonly filteredMonsters = computed(() => {
    const gen = this.activeGen();
    const sort = this.sortBy();
    const search = this.search();
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
    if (search.trim()) {
      list = list.filter(m => matchesSearch(search, this.getPokemonName(m.pokemonId), m.pokemonId));
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

  readonly selectedIds = signal(new Set<number>());

  /** The selected rules still on screen: bulk Delete and Change radius never reach a card the search has hidden. */
  readonly actionIds = computed(() =>
    visibleSelection(
      this.selectedIds(),
      this.filteredMonsters().map(item => item.uid),
    ),
  );

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
  ].map(gen => ({ ...gen, chip: readableChip(gen.color) }));

  readonly loading = signal(true);

  readonly profileAreas = signal<string[]>([]);

  // Bulk operations
  readonly selectMode = signal(false);

  readonly skeletonCards = Array.from({ length: 8 });

  readonly sortOptions: AlarmSortOption[] = [
    { label: 'POKEMON.SORT_NAME', value: 'name' },
    { label: 'POKEMON.SORT_ID', value: 'id' },
    { label: 'POKEMON.SORT_GENERATION', value: 'generation' },
    { label: 'POKEMON.SORT_EVOLUTION', value: 'evolution' },
  ];

  readonly testAlertService = inject(TestAlertService);
  async bulkDelete(): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ALARM.DELETE_SELECTED', { count: this.actionIds().length }),
        message: this.i18n.instant('ALARM.CONFIRM_BULK_DELETE_BODY'),
        title: this.i18n.plural('ALARM.CONFIRM_BULK_DELETE_TITLE', this.actionIds().length),
        warn: true,
      } as ConfirmDialogData,
    });
    const result = await firstValueFrom(ref.afterClosed());
    if (result) {
      const ids = this.actionIds();
      // Settled one at a time. A stale uid -- the row re-keyed by an edit, or removed in another tab --
      // threw out of the loop, so the deletes that had already happened went unreported and the list
      // never reloaded: the user saw nothing at all. See #603.
      let deleted = 0;
      for (const uid of ids) {
        try {
          await firstValueFrom(this.monsterService.delete(uid));
          deleted++;
        } catch {
          // Already gone, which is the outcome the user asked for.
        }
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadMonsters();
      this.snackBar.open(this.i18n.plural('ALARM.SNACK_BULK_DELETED', deleted), this.i18n.instant('COMMON.OK'), {
        duration: 3000,
      });
    }
  }

  async bulkUpdateDistance(): Promise<void> {
    const ref = this.dialog.open(DistanceDialogComponent, { width: '440px', data: { count: this.actionIds().length } });
    const distance = await firstValueFrom(ref.afterClosed());
    if (distance !== null && distance !== undefined) {
      const uids = this.actionIds();
      // The server refuses a radius that would take over an alarm the user did not select, and names
      // the one in the way. Unguarded, that rejection cleared nothing, reloaded nothing and showed
      // nothing -- indistinguishable from a successful no-op. See #641.
      let result: DistanceUpdateResult | undefined;
      try {
        result = await firstValueFrom(this.monsterService.updateBulkDistance(uids, distance));
      } catch (err) {
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DISTANCE'), this.i18n.instant('TOAST.OK'), {
          duration: 5000,
        });
        return;
      }
      this.selectedIds.set(new Set());
      this.selectMode.set(false);
      this.loadMonsters();
      this.snackBar.open(
        distanceUpdateMessage(result, this.i18n.plural('ALARM.SNACK_BULK_DISTANCE', result?.updated ?? uids.length), this.i18n),
        this.i18n.instant('COMMON.OK'),
        {
          duration: skippedAny(result) ? 6000 : 3000,
        },
      );
    }
  }

  /** The toolbar's way back from a search or filter that left nothing on screen. */
  clearFilters(): void {
    this.activeFilter.set(null);
    this.activeGen.set(null);
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE_ALL'), this.i18n.instant('COMMON.OK'), {
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
          error: (err: unknown) => {
            this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_DELETE'), this.i18n.instant('COMMON.OK'), {
              duration: 3000,
            });
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

  /**
   * A single-species rule takes the species' primary type colour (the base form's: a regional form with
   * another type keeps the base colour); "all Pokémon" has none.
   */
  edgeFor(monster: Monster): null | RuleEdge {
    return monster.pokemonId > 0 ? speciesEdge(this.masterData.getPokemonTypes(monster.pokemonId)) : null;
  }

  editMonster(monster: Monster): void {
    const ref = this.dialog.open(PokemonEditDialogComponent, {
      width: '600px',
      autoFocus: 'first-heading',
      data: monster,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadMonsters();
    });
  }

  /** Change one alarm's delivery scope from its card, without opening the whole edit dialog. */
  editScope(monster: Monster): void {
    const data: WhereSheetData = {
      profileAreas: this.profileAreas(),
      scope: scopeOf(monster.overrideLocationLabel, monster.overrideAreas, monster.distance),
    };

    this.dialog
      .open(WhereSheetComponent, whereSheetConfig(data))
      .afterClosed()
      .subscribe((scope?: AlarmScope) => {
        if (!scope) return;
        this.applyScope(monster, scope);
      });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  /**
   * The costume pill's text: "No costume" for 0, the costume's name otherwise. Never called for
   * 9000 -- "any costume" is the default and gets no pill, like every other unfiltered field.
   */
  getCostumePillText(costume: number): string {
    return costume === NO_COSTUME ? this.i18n.instant('POKEMON.NO_COSTUME_PILL') : this.masterData.getCostumeName(costume);
  }

  getGenderDisplay(gender: number): string {
    switch (gender) {
      case 1:
        return '\u2642 ' + this.i18n.instant('POKEMON.GENDER_MALE');
      case 2:
        return '\u2640 ' + this.i18n.instant('POKEMON.GENDER_FEMALE');
      case 3:
        return this.i18n.instant('POKEMON.GENDER_GENDERLESS');
      default:
        return this.i18n.instant('POKEMON.GENDER_ALL');
    }
  }

  /** The gender pill: the sign for male and female, the word for genderless. */
  getGenderPillText(gender: number): string {
    if (gender === 1) return '\u2642';
    if (gender === 2) return '\u2640';
    return this.i18n.instant('POKEMON.GENDER_GENDERLESS');
  }

  getIvDisplay(minIv: number, maxIv: number): string {
    if (minIv === -1) return this.i18n.instant('POKEMON.NO_IV_FILTER');
    return `${minIv}-${maxIv}%`;
  }

  getLeagueName(league: number): string {
    switch (league) {
      case 500:
        return this.i18n.instant('POKEMON.LEAGUE_LITTLE');
      case 1500:
        return this.i18n.instant('POKEMON.LEAGUE_GREAT');
      case 2500:
        return this.i18n.instant('POKEMON.LEAGUE_ULTRA');
      default:
        return this.i18n.instant('POKEMON.LEAGUE_CUSTOM', { cp: league });
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
        return this.i18n.instant('POKEMON.SIZE_LABEL_XXS');
      case 2:
        return this.i18n.instant('POKEMON.SIZE_LABEL_XS');
      case 3:
        return this.i18n.instant('POKEMON.SIZE_LABEL_NORMAL');
      case 4:
        return this.i18n.instant('POKEMON.SIZE_LABEL_XL');
      case 5:
        return this.i18n.instant('POKEMON.SIZE_LABEL_XXL');
      default:
        return this.i18n.instant('POKEMON.SIZE_LABEL_ANY');
    }
  }

  getSizePillText(monster: Monster): string {
    if (monster.size === monster.maxSize) {
      return this.getSizeLabel(monster.size);
    }
    return `${this.getSizeLabel(monster.size)}-${this.getSizeLabel(monster.maxSize)}`;
  }

  /** True when the auto-delete bit (clean bit 1) is set, ignoring the edit-in-place / summary bits. */
  /** Whether the card shows a level pill: only when the range is narrower than the no-filter one. */
  hasLevelFilter(monster: Pick<Monster, 'maxLevel' | 'minLevel'>): boolean {
    return hasLevelFilter(monster.minLevel, monster.maxLevel);
  }

  isAutoDelete(clean: number): boolean {
    return cleanIsAutoDelete(clean);
  }

  isEdit(clean: number): boolean {
    return cleanIsEdit(clean);
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

  /** The i18n key for a rule's mega mode, or an empty string for a base-forms rule. */
  megaLabel(evolution: number): string {
    switch (evolution) {
      case 1:
        return 'POKEMON.PVP_EVO_MEGA';
      case 2:
        return 'POKEMON.PVP_EVO_MEGA_X';
      case 3:
        return 'POKEMON.PVP_EVO_MEGA_Y';
      default:
        return '';
    }
  }

  /** The pill only appears when a filter is set, so "any" never needs rendering here. */
  minTimeParams(seconds: number): Record<string, number> {
    return minTimePillLabel(seconds).params;
  }

  minTimeText(seconds: number): string {
    return minTimePillLabel(seconds).key;
  }

  ngOnInit(): void {
    // Ensure masterdata is loaded
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadMonsters();
    this.monsterService.loadEditCapability();

    // Only used to word the inherited scope honestly: "anywhere in my areas" is a lie for a user who
    // has none selected. A failure leaves it empty, which produces the more cautious wording.
    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined, next: areas => this.profileAreas.set(areas) });
    if (this.takeAddRequest()) this.openAddDialog();
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
      autoFocus: 'first-heading',
      data: { existingRule: (uid, type) => this.describeExisting(uid, type) } satisfies AddDialogData,
      maxHeight: '90vh',
    });
    ref.afterClosed().subscribe(result => {
      if (isEditExisting(result)) this.openExisting(result.editExisting.uid, result.editExisting.trackingType);
      else if (result) this.loadMonsters();
    });
  }

  selectAll(): void {
    // Adds the cards on screen; a tick the search or a filter is hiding stays, as on the other nine lists.
    this.selectedIds.set(new Set([...this.selectedIds(), ...this.filteredMonsters().map(m => m.uid)]));
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

  private applyScope(monster: Monster, scope: AlarmScope): void {
    const fields = scopeToFields(scope);

    this.monsterService.update(monster.uid, fields).subscribe({
      error: (err: unknown) =>
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('WHERE.SCOPE_SAVE_ERROR'), this.i18n.instant('COMMON.OK'), {
          duration: 4000,
        }),
      next: () => {
        this.snackBar.open(this.i18n.instant('WHERE.SCOPE_SAVED'), this.i18n.instant('COMMON.OK'), { duration: 2500 });
        this.loadMonsters();
      },
    });
  }

  /** Names one of this list's rules for an add dialog's refusal, or null when the list holds no such rule. */
  private describeExisting(uid: number, trackingType: null | string): null | string {
    if (trackingType !== null && trackingType !== 'pokemon') return null;
    const monster = this.monsters().find(m => m.uid === uid);
    return monster
      ? ruleSummary(this.getPokemonName(monster.pokemonId), monster, this.profileAreas(), this.translate, area =>
          this.areaNames.label(area),
        )
      : null;
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

  /** An add dialog was refused because of this rule and the user chose to edit it instead. */
  private openExisting(uid: number, _trackingType: null | string): void {
    const monster = this.monsters().find(m => m.uid === uid);
    if (monster) this.editMonster(monster);
  }
}
