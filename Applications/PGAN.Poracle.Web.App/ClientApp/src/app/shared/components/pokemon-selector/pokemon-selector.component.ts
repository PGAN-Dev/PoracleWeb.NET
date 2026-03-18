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
  allPokemon = signal<PokemonEntry[]>([]);

  searchText = signal('');
  selectedPokemon = signal<PokemonEntry[]>([]);
  filteredPokemon = computed(() => {
    const search = this.searchText().toLowerCase();
    const selected = new Set(this.selectedPokemon().map(p => p.id));
    const gen = this.activeGen();
    const all = this.allPokemon();

    if (!all.length) return [];

    return all
      .filter(p => {
        if (this.multi() && selected.has(p.id)) return false;
        // When a generation is active, show all Pokemon in that range (plus "All Pokemon")
        if (gen) {
          if (p.id === 0) return true;
          const inGen = p.id >= gen.min && p.id <= gen.max;
          if (!inGen) return false;
          if (search) return p.name.toLowerCase().includes(search) || p.id.toString() === search;
          return true;
        }
        // No gen filter: original behavior
        if (!search) return p.id === 0;
        return p.name.toLowerCase().includes(search) || p.id.toString() === search;
      })
      .slice(0, 100);
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

  multi = input(false);

  searchControl = new FormControl('');

  selectionChange = output<number[]>();

  formatPokemonName(pokemon: PokemonEntry): string {
    if (pokemon.id === 0) return 'All Pokemon (ID: 0)';
    return `#${String(pokemon.id).padStart(3, '0')} ${pokemon.name}`;
  }

  getPokemonImage(id: number): string {
    return this.iconService.getPokemonUrl(id);
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
}
