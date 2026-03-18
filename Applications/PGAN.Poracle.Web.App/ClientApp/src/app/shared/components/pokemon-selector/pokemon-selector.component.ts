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

interface GenRange { label: string; min: number; max: number; }

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
    <div class="gen-filter">
      <span class="gen-label">Gen</span>
      @for (gen of generations; track gen.label) {
        <button
          class="gen-chip"
          [class.gen-active]="activeGen() === gen"
          (click)="toggleGen(gen)"
        >{{ gen.label }}</button>
      }
    </div>

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
      .gen-filter {
        display: flex;
        align-items: center;
        gap: 4px;
        margin-bottom: 12px;
        flex-wrap: wrap;
      }
      .gen-label {
        font-size: 11px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        margin-right: 2px;
      }
      .gen-chip {
        border: 1px solid var(--card-border, rgba(0,0,0,0.15));
        background: transparent;
        border-radius: 16px;
        padding: 3px 9px;
        font-size: 12px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.15s ease;
        color: var(--mat-sys-on-surface, #333);
        line-height: 1.4;
        min-width: 24px;
        text-align: center;
      }
      .gen-chip:hover {
        background: rgba(25, 118, 210, 0.08);
        border-color: #1976d2;
      }
      .gen-chip.gen-active {
        background: #1976d2;
        color: #fff;
        border-color: #1976d2;
      }
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
  activeGen = signal<GenRange | null>(null);

  readonly generations: GenRange[] = [
    { label: '1', min: 1, max: 151 },
    { label: '2', min: 152, max: 251 },
    { label: '3', min: 252, max: 386 },
    { label: '4', min: 387, max: 493 },
    { label: '5', min: 494, max: 649 },
    { label: '6', min: 650, max: 721 },
    { label: '7', min: 722, max: 809 },
    { label: '8', min: 810, max: 905 },
    { label: '9', min: 906, max: 1025 },
  ];

  filteredPokemon = computed(() => {
    const search = this.searchText().toLowerCase();
    const selected = new Set(this.selectedPokemon().map((p) => p.id));
    const gen = this.activeGen();
    const all = this.allPokemon();

    if (!all.length) return [];

    return all.filter((p) => {
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
    }).slice(0, 100);
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

  toggleGen(gen: GenRange): void {
    this.activeGen.update((current) => current === gen ? null : gen);
    this.searchControl.setValue('');
    this.searchText.set('');
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
