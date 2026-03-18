import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, ReplaySubject, forkJoin, map, tap } from 'rxjs';
import { ConfigService } from './config.service';

export interface PokemonEntry {
  id: number;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class MasterDataService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private pokemonMap = new Map<number, string>();
  private itemMap = new Map<number, string>();
  private readonly formsMap = signal(new Map<number, { id: number; name: string }[]>());
  private readonly evoBaseMap = new Map<number, number>();
  private formsLoaded = false;
  private formsLoadRequested = false;
  private loaded = false;
  private readonly ready$ = new ReplaySubject<boolean>(1);
  private loadRequested = false;

  loadData(): Observable<boolean> {
    if (!this.loadRequested) {
      this.loadRequested = true;

      forkJoin({
        pokemon: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/pokemon`),
        items: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/items`),
      }).subscribe({
        next: ({ pokemon, items }) => {
          this.pokemonMap.clear();
          if (pokemon) {
            Object.entries(pokemon).forEach(([id, name]) => {
              this.pokemonMap.set(Number(id), name as string);
            });
          }

          this.itemMap.clear();
          if (items) {
            Object.entries(items).forEach(([id, name]) => {
              this.itemMap.set(Number(id), name as string);
            });
          }

          this.loaded = true;
          this.ready$.next(true);
          this.loadForms();
        },
        error: () => {
          // Masterdata unavailable - continue without names
          this.loaded = true;
          this.loadRequested = false;
          this.ready$.next(true);
        },
      });
    }
    return this.ready$.asObservable();
  }

  private loadForms(): void {
    if (this.formsLoadRequested) return;
    this.formsLoadRequested = true;

    const url = 'https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-poracle.json';
    this.http.get<Record<string, unknown>>(url).subscribe({
      next: (data) => {
        const monsters = data['monsters'] as Record<string, { id: number; name: string; form?: { id: number; name: string }; evolutions?: { evoId: number }[] }> | undefined;
        if (!monsters) {
          this.formsLoaded = true;
          return;
        }

        const grouped = new Map<number, { id: number; name: string }[]>();
        for (const entry of Object.values(monsters)) {
          if (!entry.form || entry.form.id === 0 || entry.form.name === 'Normal') continue;
          const pokemonId = entry.id;
          if (!grouped.has(pokemonId)) {
            grouped.set(pokemonId, []);
          }
          const forms = grouped.get(pokemonId)!;
          // Avoid duplicates
          if (!forms.some(f => f.id === entry.form!.id)) {
            forms.push({ id: entry.form.id, name: entry.form.name });
          }
        }

        // Sort forms alphabetically within each Pokemon
        for (const forms of grouped.values()) {
          forms.sort((a, b) => a.name.localeCompare(b.name));
        }
        this.formsMap.set(grouped);

        // Build evolution base map: evolved Pokemon → base (first stage) Pokemon ID
        const evolvesFrom = new Map<number, number>(); // child → parent
        const seen = new Set<number>();
        for (const entry of Object.values(monsters)) {
          if (seen.has(entry.id)) continue;
          seen.add(entry.id);
          if (entry.evolutions) {
            for (const evo of entry.evolutions) {
              if (!evolvesFrom.has(evo.evoId)) {
                evolvesFrom.set(evo.evoId, entry.id);
              }
            }
          }
        }
        // Resolve chains to find the ultimate base
        for (const id of [...evolvesFrom.keys(), ...seen]) {
          let base = id;
          let safety = 5;
          while (evolvesFrom.has(base) && safety-- > 0) {
            base = evolvesFrom.get(base)!;
          }
          this.evoBaseMap.set(id, base);
        }

        this.formsLoaded = true;
      },
      error: () => {
        // Forms unavailable - continue without form names
        this.formsLoaded = true;
        this.formsLoadRequested = false;
      },
    });
  }

  getFormsForPokemon(pokemonId: number): { id: number; name: string }[] {
    return this.formsMap().get(pokemonId) ?? [];
  }

  /** Get the base (first stage) evolution ID for a Pokemon. Returns the ID itself if no chain found. */
  getBaseEvolution(id: number): number {
    return this.evoBaseMap.get(id) ?? id;
  }

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.pokemonMap.get(id) ?? `Pokemon #${id}`;
  }

  getFormName(pokemonId: number, formId: number): string {
    if (formId === 0) return '';
    const forms = this.getFormsForPokemon(pokemonId);
    const match = forms.find(f => f.id === formId);
    return match?.name ?? `Form ${formId}`;
  }

  getItemName(id: number): string {
    return this.itemMap.get(id) ?? `Item #${id}`;
  }

  getAllPokemon(): PokemonEntry[] {
    const entries: PokemonEntry[] = [{ id: 0, name: 'All Pokemon' }];
    this.pokemonMap.forEach((name, id) => {
      entries.push({ id, name });
    });
    entries.sort((a, b) => a.id - b.id);
    return entries;
  }

  getAllPokemon$(): Observable<PokemonEntry[]> {
    return this.loadData().pipe(map(() => this.getAllPokemon()));
  }

  isLoaded(): boolean {
    return this.loaded;
  }
}
