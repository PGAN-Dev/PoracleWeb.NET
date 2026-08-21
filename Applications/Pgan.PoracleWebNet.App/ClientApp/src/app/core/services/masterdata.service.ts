import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject, signal } from '@angular/core';
import { Observable, ReplaySubject, catchError, forkJoin, map, of } from 'rxjs';

import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { POKEMON_TYPE_NAMES_BY_ID } from '../../shared/utils/pokemon-types';

export interface PokemonEntry {
  id: number;
  name: string;
  types?: string[];
}

/** One entry of the monster map, keyed `"{pokemonId}_{formId}"`. */
interface MonsterEntry {
  evolutions?: { evoId: number }[];
  form?: { id: number; name: string };
  id: number;
  name: string;
  types?: { id: number; name: string }[];
}

/**
 * A translation key that came back as itself, e.g. `poke_25`. PoracleNG returns the key when neither
 * the requested locale nor its English fallback has the string, which happens when its game-data
 * locale download failed. Showing "poke_25" would be worse than the English name we already have.
 */
const UNTRANSLATED_KEY = /^(poke|poke_type|form)_\d+$/;

@Injectable({ providedIn: 'root' })
export class MasterDataService {
  private readonly config = inject(ConfigService);
  private readonly evoBaseMap = new Map<number, number>();

  private readonly formsMap = signal(new Map<number, { id: number; name: string }[]>());
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18nService);
  private itemMap = new Map<number, string>();
  private loaded = false;
  /** Locale of the data currently in the maps, so a display-language change can be detected. */
  private loadedLocale = '';
  private loadRequested = false;
  private moveMap = new Map<number, string>();
  private pokemonMap = new Map<number, string>();
  private readonly ready$ = new ReplaySubject<boolean>(1);
  private readonly typeLabels = signal(new Map<string, string>());
  private readonly typesMap = signal(new Map<number, string[]>());

  constructor() {
    // Pokemon, type and form names are all translated server-side, so a display-language change
    // invalidates every map. Re-emitting on ready$ live-updates anything already subscribed
    // through getAllPokemon$(), which is how open selectors pick the new names up.
    effect(() => {
      const locale = this.i18n.currentLang();
      if (this.loadRequested && locale !== this.loadedLocale) {
        this.fetch();
      }
    });
  }

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

  getAllTypes(): string[] {
    const typeSet = new Set<string>();
    for (const types of this.typesMap().values()) {
      for (const t of types) typeSet.add(t);
    }
    return [...typeSet].sort();
  }

  /** Get the base (first stage) evolution ID for a Pokemon. Returns the ID itself if no chain found. */
  getBaseEvolution(id: number): number {
    return this.evoBaseMap.get(id) ?? id;
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

  getMoveName(id: number): string {
    return this.moveMap.get(id) ?? `Move #${id}`;
  }

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.pokemonMap.get(id) ?? `Pokemon #${id}`;
  }

  getPokemonTypes(id: number): string[] {
    return this.typesMap().get(id) ?? [];
  }

  /**
   * The display label for a type. Takes the English name that everything else keys on and returns
   * the translation for the current display language, falling back to the English name itself.
   */
  getTypeLabel(englishName: string): string {
    return this.typeLabels().get(englishName) ?? englishName;
  }

  isLoaded(): boolean {
    return this.loaded;
  }

  loadData(): Observable<boolean> {
    if (!this.loadRequested) {
      this.loadRequested = true;
      this.fetch();
    }
    return this.ready$.asObservable();
  }

  /**
   * Rebuilds the maps from the monster payload: localized names, types, forms and evolution chains.
   * A null payload (upstream unreachable) leaves the English names from /api/masterdata/pokemon in
   * place rather than blanking the selector.
   */
  private applyMonsters(monsters: null | Record<string, MonsterEntry>): void {
    if (!monsters) return;

    const namesById = new Map<number, string>();
    const grouped = new Map<number, { id: number; name: string }[]>();
    const typeMap = new Map<number, string[]>();
    const typeLabelMap = new Map<string, string>();

    for (const [key, entry] of Object.entries(monsters)) {
      if (!entry || typeof entry.id !== 'number') continue;

      // Form 0 is the species' own name; other forms carry it too, so prefer form 0 when present.
      if (entry.name && !UNTRANSLATED_KEY.test(entry.name) && (key.endsWith('_0') || !namesById.has(entry.id))) {
        namesById.set(entry.id, entry.name);
      }

      // Skip only the synthetic id-0 "any" pseudo-form. Real forms (including the
      // base "Normal"/regional-default form, e.g. Unova Stunfisk) are kept so they
      // can be tracked distinctly from regional variants like Galarian.
      if (entry.form && entry.form.id !== 0 && entry.form.name) {
        const forms = grouped.get(entry.id) ?? [];
        if (!forms.some(f => f.id === entry.form!.id)) {
          forms.push({ id: entry.form.id, name: entry.form.name });
        }
        grouped.set(entry.id, forms);
      }

      // Types come from the base form only; form variants repeat them.
      if (entry.types?.length && !typeMap.has(entry.id)) {
        const english: string[] = [];
        for (const t of entry.types) {
          // The id is the stable identity - icons and filters key on the English name, so a
          // localized label is only ever recorded alongside it, never in its place.
          const name = POKEMON_TYPE_NAMES_BY_ID[t.id] ?? t.name;
          if (!name) continue;
          english.push(name);
          if (t.name && !UNTRANSLATED_KEY.test(t.name)) {
            typeLabelMap.set(name, t.name);
          }
        }
        if (english.length) typeMap.set(entry.id, english);
      }
    }

    // Drop a lone "Normal" form: when a species' only real form is its base/regional
    // default, the synthetic "All Forms" option already covers it, so listing it adds
    // noise. Keep "Normal" only when sibling variants (Galarian, Alolan, etc.) exist
    // so users can target the base form on its own.
    //
    // The name is matched by prefix because it is now translated. Across the locales this UI
    // offers, prod serves "Normal" for en/de/fr/es/pl/sv and "Normale" for it; nl, pt, pt-BR and
    // da have no Poracle translation and fall back to English. Species whose only real form is
    // something else - Koraidon's "Apex Build", Miraidon's "Ultimate Mode", the two exceptions in
    // live data - are unaffected. A locale that translates it to something else entirely shows one
    // redundant chip; it cannot hide a form.
    for (const [pokemonId, forms] of grouped) {
      if (forms.length === 1 && /^normal/i.test(forms[0].name)) {
        grouped.delete(pokemonId);
      }
    }

    for (const forms of grouped.values()) {
      forms.sort((a, b) => a.name.localeCompare(b.name));
    }

    namesById.forEach((name, id) => this.pokemonMap.set(id, name));
    this.formsMap.set(grouped);
    this.typesMap.set(typeMap);
    this.typeLabels.set(typeLabelMap);
    this.buildEvolutionMap(monsters);
  }

  /** Resolves each species to the first stage of its evolution chain. */
  private buildEvolutionMap(monsters: Record<string, MonsterEntry>): void {
    const evolvesFrom = new Map<number, number>(); // child -> parent
    const seen = new Set<number>();
    for (const entry of Object.values(monsters)) {
      if (!entry || seen.has(entry.id)) continue;
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
  }

  /**
   * Loads every map for the current display language.
   *
   * Monsters come from PoracleNG (via our API) because it owns the translations; items and moves
   * stay on the English masterfile, which has no translated equivalent upstream. A monster failure
   * is caught rather than left to cancel the forkJoin, so English names still land.
   */
  private fetch(): void {
    const locale = this.i18n.currentLang();
    this.loadedLocale = locale;

    forkJoin({
      items: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/items`),
      monsters: this.http
        .get<Record<string, MonsterEntry>>(`${this.config.apiHost}/api/masterdata/monsters`, { params: { locale } })
        .pipe(catchError(() => of(null))),
      moves: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/moves`),
      pokemon: this.http.get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/pokemon`),
    }).subscribe({
      error: () => {
        // Masterdata unavailable - continue without names
        this.loaded = true;
        this.loadRequested = false;
        this.ready$.next(true);
      },
      next: ({ items, monsters, moves, pokemon }) => {
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

        this.moveMap.clear();
        if (moves) {
          Object.entries(moves).forEach(([id, name]) => {
            this.moveMap.set(Number(id), name as string);
          });
        }

        this.applyMonsters(monsters);

        this.loaded = true;
        this.ready$.next(true);
      },
    });
  }
}
