import {
  Component,
  DestroyRef,
  inject,
  signal,
  computed,
  input,
  output,
  OnInit,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MasterDataService, PokemonEntry } from '../../../core/services/masterdata.service';

@Component({
  selector: 'app-pokemon-selector',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
  ],
  template: `
    <mat-form-field class="full-width" appearance="outline">
      <mat-label>Search Pokemon</mat-label>
      <input
        matInput
        [formControl]="searchControl"
        [matAutocomplete]="auto"
        placeholder="Type name or ID..."
      />
      <mat-autocomplete #auto="matAutocomplete" (optionSelected)="onSelected($event)">
        @for (pokemon of filteredPokemon(); track pokemon.id) {
          <mat-option [value]="pokemon" [class.all-pokemon-option]="pokemon.id === 0">
            <div class="pokemon-option">
              @if (pokemon.id === 0) {
                <mat-icon class="all-pokemon-icon">select_all</mat-icon>
              } @else {
                <img
                  [src]="getPokemonImage(pokemon.id)"
                  [alt]="pokemon.name"
                  class="pokemon-option-img"
                  (error)="onImageError($event)"
                />
              }
              <span>{{ formatPokemonName(pokemon) }}</span>
              @if (pokemon.id > 0) {
                <span class="pokemon-option-id">#{{ padId(pokemon.id) }}</span>
              }
            </div>
          </mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>

    @if (multi()) {
      <mat-chip-set class="selected-chips">
        @for (pokemon of selectedPokemon(); track pokemon.id) {
          <mat-chip (removed)="removePokemon(pokemon.id)" [class.all-pokemon-chip]="pokemon.id === 0">
            @if (pokemon.id === 0) {
              <mat-icon matChipAvatar>select_all</mat-icon>
            } @else {
              <img
                matChipAvatar
                [src]="getPokemonImage(pokemon.id)"
                [alt]="pokemon.name"
                class="chip-avatar"
                (error)="onImageError($event)"
              />
            }
            {{ formatPokemonName(pokemon) }}
            <button matChipRemove>
              <mat-icon>cancel</mat-icon>
            </button>
          </mat-chip>
        }
      </mat-chip-set>
    }
  `,
  styles: [
    `
      .full-width { width: 100%; }
      .pokemon-option { display: flex; align-items: center; gap: 8px; }
      .pokemon-option-img { width: 32px; height: 32px; object-fit: contain; }
      .pokemon-option-id { margin-left: auto; color: rgba(0,0,0,0.54); font-size: 12px; }
      .all-pokemon-icon { width: 32px; height: 32px; font-size: 28px; color: #1565c0; }
      .all-pokemon-option { border-bottom: 1px solid rgba(0,0,0,0.12); font-weight: 500; }
      .all-pokemon-chip { background-color: #e3f2fd !important; font-weight: 500; }
      .selected-chips { margin-top: 8px; }
      .chip-avatar { width: 24px !important; height: 24px !important; object-fit: contain; }
    `,
  ],
})
export class PokemonSelectorComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly masterData = inject(MasterDataService);

  multi = input(false);
  selectionChange = output<number[]>();

  searchControl = new FormControl('');
  allPokemon = signal<PokemonEntry[]>([]);
  selectedPokemon = signal<PokemonEntry[]>([]);
  searchText = signal('');

  filteredPokemon = computed(() => {
    const search = this.searchText().toLowerCase();
    const selected = new Set(this.selectedPokemon().map((p) => p.id));
    const all = this.allPokemon();

    if (!all.length) return [];

    return all.filter((p) => {
      if (this.multi() && selected.has(p.id)) return false;
      if (!search) return p.id === 0; // Show only "All Pokemon" when no search
      return p.name.toLowerCase().includes(search) || p.id.toString() === search;
    }).slice(0, 50); // Limit to 50 results for performance
  });

  ngOnInit(): void {
    this.masterData.getAllPokemon$().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((pokemon) => {
      this.allPokemon.set(pokemon);
    });

    this.searchControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      if (typeof value === 'string') {
        this.searchText.set(value);
      }
    });
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    const pokemon: PokemonEntry = event.option.value;
    if (this.multi()) {
      this.selectedPokemon.update((list) => [...list, pokemon]);
      this.searchControl.setValue('');
      this.searchText.set('');
      this.selectionChange.emit(this.selectedPokemon().map((p) => p.id));
    } else {
      this.selectedPokemon.set([pokemon]);
      this.searchControl.setValue(this.formatPokemonName(pokemon), { emitEvent: false });
      this.selectionChange.emit([pokemon.id]);
    }
  }

  removePokemon(id: number): void {
    this.selectedPokemon.update((list) => list.filter((p) => p.id !== id));
    this.selectionChange.emit(this.selectedPokemon().map((p) => p.id));
  }

  formatPokemonName(pokemon: PokemonEntry): string {
    if (pokemon.id === 0) return 'All Pokemon (ID: 0)';
    return `#${String(pokemon.id).padStart(3, '0')} ${pokemon.name}`;
  }

  padId(id: number): string {
    return String(id).padStart(3, '0');
  }

  getPokemonImage(id: number): string {
    if (id === 0) return '';
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/${id}.png`;
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }
}
