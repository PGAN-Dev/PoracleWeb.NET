import { Injectable, inject } from '@angular/core';
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

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.pokemonMap.get(id) ?? `Pokemon #${id}`;
  }

  getFormName(pokemonId: number, formId: number): string {
    if (formId === 0) return '';
    return `Form ${formId}`;
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
