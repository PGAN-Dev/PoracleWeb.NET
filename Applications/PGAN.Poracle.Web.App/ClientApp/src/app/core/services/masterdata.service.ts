import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, ReplaySubject, forkJoin, map } from 'rxjs';

import { ConfigService } from './config.service';

export interface PokemonEntry {
  id: number;
  name: string;
  types?: string[];
}

@Injectable({ providedIn: 'root' })
export class MasterDataService {
  private readonly config = inject(ConfigService);
  private readonly evoBaseMap = new Map<number, number>();

  private formsLoaded = false;
  private formsLoadRequested = false;
  private readonly formsMap = signal(new Map<number, { id: number; name: string }[]>());
  private readonly typesMap = signal(new Map<number, string[]>());
  private readonly http = inject(HttpClient);
  private itemMap = new Map<number, string>();
  private loaded = false;
  private loadRequested = false;
  private pokemonMap = new Map<number, string>();
  private readonly ready$ = new ReplaySubject<boolean>(1);

  getAllItems(): { id: number; name: string }[] {
    const entries: { id: number; name: string }[] = [];
    this.itemMap.forEach((name, id) => {
      entries.push({ id, name });
    });
    entries.sort((a, b) => a.name.localeCompare(b.name));
    return entries;
  }

  getAllPokemon(): PokemonEntry[] {
    const types = this.typesMap();
    const entries: PokemonEntry[] = [{ id: 0, name: 'All Pokemon' }];
    this.pokemonMap.forEach((name, id) => {
      entries.push({ id, name, types: types.get(id) });
    });
    entries.sort((a, b) => a.id - b.id);
    return entries;
  }

  getAllPokemon$(): Observable<PokemonEntry[]> {
    return this.loadData().pipe(map(() => this.getAllPokemon()));
  }

  /** Get the base (first stage) evolution ID for a Pokemon. Returns the ID itself if no chain found. */
  getBaseEvolution(id: number): number {
    return this.evoBaseMap.get(id) ?? id;
  }

  getAllTypes(): string[] {
    const typeSet = new Set<string>();
    for (const types of this.typesMap().values()) {
      for (const t of types) typeSet.add(t);
    }
    return [...typeSet].sort();
  }

  getFormName(pokemonId: number, formId: number): string {
    if (formId === 0) return '';
    const forms = this.getFormsForPokemon(pokemonId);
    const match = forms.find(f => f.id === formId);
    return match?.name ?? `Form ${formId}`;
  }

  getFormsForPokemon(pokemonId: number): { id: number; name: string }[] {
    return this.formsMap().get(pokemonId) ?? [];
  }

  getItemName(id: number): string {
    return this.itemMap.get(id) ?? `Item #${id}`;
  }

  getPokemonTypes(id: number): string[] {
    return this.typesMap().get(id) ?? [];
  }

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.pokemonMap.get(id) ?? `Pokemon #${id}`;
  }

  isLoaded(): boolean {
    return this.loaded;
  }

  loadData(): Observable<boolean> {
    if (!this.loadRequested) {
      this.loadRequested = true;

      forkJoin({
        items: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/items`),
        pokemon: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/pokemon`),
      }).subscribe({
        error: () => {
          // Masterdata unavailable - continue without names
          this.loaded = true;
          this.loadRequested = false;
          this.ready$.next(true);
        },
        next: ({ items, pokemon }) => {
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
      });
    }
    return this.ready$.asObservable();
  }

  private loadForms(): void {
    if (this.formsLoadRequested) return;
    this.formsLoadRequested = true;

    const url = 'https://raw.githubusercontent.com/WatWowMap/Masterfile-Generator/master/master-latest-poracle.json';
    this.http.get<Record<string, unknown>>(url).subscribe({
      error: () => {
        // Forms unavailable - continue without form names
        this.formsLoaded = true;
        this.formsLoadRequested = false;
      },
      next: data => {
        const monsters = data['monsters'] as
          | Record<
              string,
              {
                id: number;
                name: string;
                form?: { id: number; name: string };
                evolutions?: { evoId: number }[];
                types?: { id: number; name: string }[];
              }
            >
          | undefined;
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

        // Build types map: Pokemon ID → type names (use base form only, skip form variants)
        const typeMap = new Map<number, string[]>();
        for (const entry of Object.values(monsters)) {
          if (typeMap.has(entry.id)) continue;
          if (entry.types?.length) {
            typeMap.set(entry.id, entry.types.map(t => t.name));
          }
        }
        this.typesMap.set(typeMap);

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
    });
  }
}
