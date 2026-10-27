import { Component, DestroyRef, inject, signal, computed, input, output, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

import { I18nService } from '../../../core/services/i18n.service';
import { IconService } from '../../../core/services/icon.service';
import { MasterDataService, PokemonEntry } from '../../../core/services/masterdata.service';
import { PokemonAvailabilityService } from '../../../core/services/pokemon-availability.service';
import { includesMinior } from '../../utils/minior';
import { BulkMatchResult, BulkMatchSuggestion, matchPokemonBulkText } from '../../utils/pokemon-bulk-match';

/** Distinct ids for each picker's expander, since a quest dialog holds three selectors at once. */
let nextPickersId = 0;

/** Selected chips shown before the rest fold behind "Show all": a whole generation is 151 of them. */
const CHIP_LIMIT = 30;

/** The search dropdown's length. The tile grid is not capped: "Select all" must add what it shows. */
const DROPDOWN_LIMIT = 200;

interface GenRange {
  label: string;
  max: number;
  min: number;
}

@Component({
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatChipsModule, MatFormFieldModule, MatInputModule, MatIconModule, TranslatePipe],
  selector: 'app-pokemon-selector',
  standalone: true,
  styleUrl: './pokemon-selector.component.scss',
  templateUrl: './pokemon-selector.component.html',
})
export class PokemonSelectorComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);
  activeGen = signal<GenRange | null>(null);

  activeType = signal<string | null>(null);
  allowedIds = input<number[] | null>(null);
  allPokemon = signal<PokemonEntry[]>([]);
  readonly availability = inject(PokemonAvailabilityService);

  availableTypes = computed(() => this.masterData.getAllTypes());
  bulkControl = new FormControl('');
  bulkMode = signal(false);
  bulkResult = signal<BulkMatchResult | null>(null);
  readonly chipsExpanded = signal(false);
  filterSpawning = signal(false);
  multi = input(false);
  searchText = signal('');
  selectedPokemon = signal<PokemonEntry[]>([]);
  selectedIds = computed(() => new Set(this.selectedPokemon().map(p => p.id)));
  showAvailability = computed(() => this.availability.enabled());
  showTileGrid = computed(() => this.multi() && (this.activeGen() != null || this.activeType() != null || this.filterSpawning()));

  filteredPokemon = computed(() => {
    const search = this.searchText().toLowerCase();
    const selected = this.selectedIds();
    const gen = this.activeGen();
    const type = this.activeType();
    const spawning = this.filterSpawning();
    const tileMode = this.showTileGrid();
    const all = this.allPokemon();
    const allowed = this.allowedIds();
    const hasFilter = gen != null || type != null || spawning;

    if (!all.length) return [];

    const filtered = all
      .filter(p => {
        // If allowedIds is set, restrict to those IDs (plus keep ID 0 for "All Pokemon")
        if (allowed && allowed.length > 0 && p.id !== 0 && !allowed.includes(p.id)) return false;
        // In tile grid mode, keep selected pokemon visible (they show as selected tiles)
        // In autocomplete mode, hide already-selected from dropdown
        if (this.multi() && !tileMode && selected.has(p.id)) return false;
        if (p.id === 0) return !tileMode && (hasFilter || !search);
        // Spawning filter
        if (spawning && !this.availability.isAvailable(p.id)) return false;
        // Type filter
        if (type) {
          const pokemonTypes = this.masterData.getPokemonTypes(p.id);
          if (!pokemonTypes.includes(type)) return false;
        }
        // Generation filter
        if (gen) {
          const inGen = p.id >= gen.min && p.id <= gen.max;
          if (!inGen) return false;
        }
        // Search filter
        if (search) return p.name.toLowerCase().includes(search) || p.id.toString() === search;
        // Show all when a filter is active, otherwise require search
        return hasFilter;
      })
      .slice(0, tileMode ? undefined : DROPDOWN_LIMIT);

    // Sort available Pokemon first when any filter is active
    if (hasFilter && this.showAvailability()) {
      filtered.sort((a, b) => {
        if (a.id === 0) return -1;
        if (b.id === 0) return 1;
        const aAvail = this.availability.isAvailable(a.id) ? 0 : 1;
        const bAvail = this.availability.isAvailable(b.id) ? 0 : 1;
        if (aAvail !== bAvail) return aAvail - bAvail;
        return a.id - b.id;
      });
    }

    return filtered;
  });

  readonly generations: GenRange[] = [
    { label: '1', max: 151, min: 1 },
    { label: '2', max: 251, min: 152 },
    { label: '3', max: 386, min: 252 },
    { label: '4', max: 493, min: 387 },
    { label: '5', max: 649, min: 494 },
    { label: '6', max: 721, min: 650 },
    { label: '7', max: 809, min: 722 },
    { label: '8', max: 905, min: 810 },
    { label: '9', max: 1025, min: 906 },
  ];

  readonly hiddenChipCount = computed(() => Math.max(0, this.selectedPokemon().length - CHIP_LIMIT));

  /**
   * Ids to show as already picked when the selector opens, for an editor that holds a stored choice.
   * Read once, when the Pokémon list arrives; nothing is emitted for them.
   */
  initialIds = input<number[]>([]);

  /**
   * Set by pickers whose alert has IV, CP and level filters (Pokemon alerts). Raid, nest and quest
   * pickers leave it off: those alerts have no such filters, so the Minior note would mean nothing there.
   */
  ivFilters = input(false);

  /**
   * The generation, type and live chips sit behind "Add by type or generation". Search is what almost
   * everyone opens the dialog for, and 27 chips used to stand between them and the field.
   */
  readonly pickersId = `pokemon-pickers-${nextPickersId++}`;

  readonly pickersOpen = signal(false);

  searchControl = new FormControl('');

  selectionChange = output<number[]>();

  /** Shown the moment Minior is picked for an alert with IV filters. See `utils/minior.ts`. */
  showMiniorNote = computed(() => this.ivFilters() && includesMinior(this.selectedIds()));

  /** The species the tile grid shows for the current filter and search, "All Pokémon" aside. */
  readonly shownSpecies = computed(() => (this.showTileGrid() ? this.filteredPokemon().filter(p => p.id !== 0) : []));

  readonly shownUnselectedCount = computed(() => {
    const selected = this.selectedIds();
    return this.shownSpecies().filter(p => !selected.has(p.id)).length;
  });

  readonly shownSelectedCount = computed(() => this.shownSpecies().length - this.shownUnselectedCount());

  readonly visibleChips = computed(() => (this.chipsExpanded() ? this.selectedPokemon() : this.selectedPokemon().slice(0, CHIP_LIMIT)));

  addSuggestion(suggestion: BulkMatchSuggestion, candidate: PokemonEntry): void {
    if (!this.selectedIds().has(candidate.id)) {
      this.selectedPokemon.update(list => [...list, candidate]);
      this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
    }
    // The suggestion is resolved either way -- leaving it on screen after its chip was clicked
    // would read as "that didn't work".
    this.bulkResult.update(result =>
      result ? { ...result, suggestions: result.suggestions.filter(s => s.token !== suggestion.token) } : result,
    );
  }

  clearSelection(): void {
    this.selectedPokemon.set([]);
    this.chipsExpanded.set(false);
    this.emitSelection();
  }

  /** Removes every species the grid shows from the selection, leaving picks from other filters alone. */
  deselectAllShown(): void {
    const shown = new Set(this.shownSpecies().map(p => p.id));
    if (!this.selectedPokemon().some(p => shown.has(p.id))) return;
    this.selectedPokemon.update(list => list.filter(p => !shown.has(p.id)));
    this.emitSelection();
  }

  formatPokemonName(pokemon: PokemonEntry): string {
    if (pokemon.id === 0) return this.i18n.instant('POKEMON_SELECTOR.ALL_POKEMON');
    return `#${String(pokemon.id).padStart(3, '0')} ${pokemon.name}`;
  }

  getPokemonImage(id: number): string {
    return this.iconService.getPokemonUrl(id);
  }

  getTypeIcon(type: string): string {
    return this.iconService.getTypeUrl(type);
  }

  ngOnInit(): void {
    this.availability.load();

    this.masterData
      .getAllPokemon$()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(pokemon => {
        this.allPokemon.set(pokemon);
        this.applyInitialSelection(pokemon);
      });

    this.searchControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value => {
      if (typeof value === 'string') {
        this.searchText.set(value);
      }
    });

    // A stale "3 not recognized" summary reads as a fresh answer to edited text -- clear it the
    // moment the textarea changes, so it only ever reflects the text Match & Add last ran against.
    this.bulkControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.bulkResult.set(null);
    });
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    const pokemon: PokemonEntry = event.option.value;
    if (this.multi()) {
      this.selectedPokemon.update(list => [...list, pokemon]);
      this.searchControl.setValue('');
      this.searchText.set('');
      this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
    } else {
      this.selectedPokemon.set([pokemon]);
      this.searchControl.setValue(this.formatPokemonName(pokemon), { emitEvent: false });
      this.selectionChange.emit([pokemon.id]);
    }
  }

  padId(id: number): string {
    return String(id).padStart(3, '0');
  }

  removePokemon(id: number): void {
    this.selectedPokemon.update(list => list.filter(p => p.id !== id));
    this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
  }

  runBulkMatch(): void {
    const text = this.bulkControl.value ?? '';
    if (!text.trim()) return;

    const allowed = this.allowedIds();
    const result = matchPokemonBulkText(text, this.allPokemon(), this.selectedIds(), allowed ? new Set(allowed) : null);
    if (result.matched.length > 0) {
      this.selectedPokemon.update(list => [...list, ...result.matched]);
      this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
    }
    this.bulkResult.set(result);
  }

  /** Adds every species the grid shows (the generation, type, Live filter and search together) in one go. */
  selectAllShown(): void {
    const selected = this.selectedIds();
    const add = this.shownSpecies().filter(p => !selected.has(p.id));
    if (!add.length) return;
    this.selectedPokemon.update(list => [...list, ...add]);
    this.emitSelection();
  }

  toggleBulkMode(): void {
    this.bulkMode.update(v => !v);
    this.bulkControl.setValue('');
    this.bulkResult.set(null);
  }

  toggleChips(): void {
    this.chipsExpanded.update(open => !open);
  }

  toggleGen(gen: GenRange): void {
    this.activeGen.update(current => (current === gen ? null : gen));
    this.searchControl.setValue('');
    this.searchText.set('');
  }

  /**
   * Closing the pickers drops whatever they were filtering by. Otherwise the tile grid they produced would
   * stay on screen with the chips that explain it hidden. What was picked from the tiles stays picked.
   */
  togglePickers(): void {
    if (this.pickersOpen()) {
      this.activeGen.set(null);
      this.activeType.set(null);
      this.filterSpawning.set(false);
    }
    this.pickersOpen.update(open => !open);
  }

  toggleSpawning(): void {
    this.filterSpawning.update(v => !v);
    this.searchControl.setValue('');
    this.searchText.set('');
  }

  toggleTile(pokemon: PokemonEntry): void {
    if (this.selectedIds().has(pokemon.id)) {
      this.removePokemon(pokemon.id);
    } else {
      this.selectedPokemon.update(list => [...list, pokemon]);
      this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
    }
  }

  toggleType(type: string): void {
    this.activeType.update(current => (current === type ? null : type));
    this.searchControl.setValue('');
    this.searchText.set('');
  }

  /**
   * Display text for a type chip. The chip's value stays the English name - the icon lookup and the
   * filter comparison both key on it - so only the label is translated.
   */
  typeLabel(type: string): string {
    return this.masterData.getTypeLabel(type);
  }

  private applyInitialSelection(pokemon: PokemonEntry[]): void {
    const ids = this.initialIds();
    if (!ids.length || this.selectedPokemon().length) return;
    const picked = ids.map(id => pokemon.find(p => p.id === id)).filter((p): p is PokemonEntry => !!p);
    if (!picked.length) return;
    this.selectedPokemon.set(this.multi() ? picked : [picked[0]]);
    if (!this.multi()) {
      this.searchControl.setValue(this.formatPokemonName(picked[0]), { emitEvent: false });
    }
  }

  private emitSelection(): void {
    this.selectionChange.emit(this.selectedPokemon().map(p => p.id));
  }
}
