import { Component, DestroyRef, inject, signal, computed, input, output, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import { IconService } from '../../../core/services/icon.service';
import { MasterDataService, PokemonEntry } from '../../../core/services/masterdata.service';

interface GenRange {
  label: string;
  max: number;
  min: number;
}

@Component({
  imports: [ReactiveFormsModule, MatAutocompleteModule, MatChipsModule, MatFormFieldModule, MatInputModule, MatIconModule],
  selector: 'app-pokemon-selector',
  standalone: true,
  styleUrl: './pokemon-selector.component.scss',
  templateUrl: './pokemon-selector.component.html',
})
export class PokemonSelectorComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly iconService = inject(IconService);
  private readonly masterData = inject(MasterDataService);

  activeGen = signal<GenRange | null>(null);
  activeType = signal<string | null>(null);
  allowedIds = input<number[] | null>(null);
  allPokemon = signal<PokemonEntry[]>([]);

  availableTypes = computed(() => this.masterData.getAllTypes());
  searchText = signal('');
  selectedPokemon = signal<PokemonEntry[]>([]);
  selectedIds = computed(() => new Set(this.selectedPokemon().map(p => p.id)));
  multi = input(false);
  showTileGrid = computed(() => this.multi() && (this.activeGen() != null || this.activeType() != null));

  filteredPokemon = computed(() => {
    const search = this.searchText().toLowerCase();
    const selected = this.selectedIds();
    const gen = this.activeGen();
    const type = this.activeType();
    const tileMode = this.showTileGrid();
    const all = this.allPokemon();
    const allowed = this.allowedIds();

    if (!all.length) return [];

    return all
      .filter(p => {
        // If allowedIds is set, restrict to those IDs (plus keep ID 0 for "All Pokemon")
        if (allowed && allowed.length > 0 && p.id !== 0 && !allowed.includes(p.id)) return false;
        // In tile grid mode, keep selected pokemon visible (they show as selected tiles)
        // In autocomplete mode, hide already-selected from dropdown
        if (this.multi() && !tileMode && selected.has(p.id)) return false;
        if (p.id === 0) return !tileMode && (gen != null || type != null || !search);
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
        return gen != null || type != null;
      })
      .slice(0, 200);
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

  searchControl = new FormControl('');

  selectionChange = output<number[]>();

  formatPokemonName(pokemon: PokemonEntry): string {
    if (pokemon.id === 0) return 'All Pokemon (ID: 0)';
    return `#${String(pokemon.id).padStart(3, '0')} ${pokemon.name}`;
  }

  getPokemonImage(id: number): string {
    return this.iconService.getPokemonUrl(id);
  }

  getTypeIcon(type: string): string {
    return this.iconService.getTypeUrl(type);
  }

  ngOnInit(): void {
    this.masterData
      .getAllPokemon$()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(pokemon => {
        this.allPokemon.set(pokemon);
      });

    this.searchControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value => {
      if (typeof value === 'string') {
        this.searchText.set(value);
      }
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

  toggleGen(gen: GenRange): void {
    this.activeGen.update(current => (current === gen ? null : gen));
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
}
