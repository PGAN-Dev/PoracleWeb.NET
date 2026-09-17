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

/**
 * One entry of the grunt map, keyed by grunt id. Several ids share one `grunt_type` -- a grunt has a
 * male and a female entry, and Giovanni and the leaders have a ticketed and an unticketed one -- so
 * `grunt_type` is the join column, not the key.
 */
interface GruntEntry {
  gender?: number;
  grunt_type?: string;
  short_name?: string;
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
  /**
   * The name maps are signals, not plain Maps, because they are filled long after the first paint.
   * Every reader below runs inside a template or a computed, so a signal read is what makes the
   * alarm cards repaint when the names finally land -- a mutated Map would leave them showing
   * `Pokemon #1` until something unrelated redrew them. See the late-arrival spec.
   */
  private readonly costumeMap = signal(new Map<number, string>());
  private readonly evoBaseMap = new Map<number, number>();

  private readonly formsMap = signal(new Map<number, { id: number; name: string }[]>());
  /**
   * Grunt display names, keyed `"{grunt_type}:{gender}"`. Only entries that identify exactly one
   * grunt are here -- see `applyGrunts`.
   */
  private readonly gruntMap = signal(new Map<string, string>());
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18nService);
  private readonly itemMap = signal(new Map<number, string>());
  private loaded = false;
  /** Locale of the data currently in the maps, so a display-language change can be detected. */
  private loadedLocale = '';
  private loadRequested = false;
  private readonly moveMap = signal(new Map<number, string>());
  private readonly pokemonMap = signal(new Map<number, string>());
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

  /** Whether any costume names loaded. False means the dialogs offer only the two sentinels. */
  costumesAvailable(): boolean {
    return this.costumeMap().size > 0;
  }

  getAllItems(): { id: number; name: string }[] {
    const entries: { id: number; name: string }[] = [];
    this.itemMap().forEach((name, id) => {
      entries.push({ id, name });
    });
    entries.sort((a, b) => a.name.localeCompare(b.name));
    return entries;
  }

  getAllPokemon(): PokemonEntry[] {
    const types = this.typesMap();
    const entries: PokemonEntry[] = [{ id: 0, name: 'All Pokemon' }];
    this.pokemonMap().forEach((name, id) => {
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

  /**
   * The label for a costume id. An id the masterfile does not name -- a costume Niantic shipped
   * before WatWowMap regenerated -- renders as "Costume 88" rather than blank, mirroring how an
   * unknown form renders.
   */
  getCostumeName(id: number): string {
    return this.costumeMap().get(id) ?? this.i18n.instant('POKEMON.COSTUME_FALLBACK', { id });
  }

  /**
   * The named costumes, newest first.
   *
   * Costume tracking is event-driven -- the costume someone wants is almost always the one currently
   * in the game -- so descending id puts the likely answer at the top of the list. The "any" and "no
   * costume" choices are not in here: they are sentinels the dialogs pin above the named list.
   */
  getCostumes(): { id: number; name: string }[] {
    const entries: { id: number; name: string }[] = [];
    this.costumeMap().forEach((name, id) => {
      entries.push({ id, name });
    });
    entries.sort((a, b) => b.id - a.id);
    return entries;
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

  /**
   * The upstream display name for one grunt, or null when this Poracle does not name it unambiguously
   * and the caller should use its own label.
   */
  getGruntName(gruntType: null | string, gender: number | undefined): null | string {
    if (!gruntType) return null;
    return this.gruntMap().get(`${gruntType}:${gender ?? 0}`) ?? null;
  }

  getItemName(id: number): string {
    return this.itemMap().get(id) ?? `Item #${id}`;
  }

  getMoveName(id: number): string {
    return this.moveMap().get(id) ?? `Move #${id}`;
  }

  getPokemonName(id: number): string {
    if (id === 0) return 'All Pokemon';
    return this.pokemonMap().get(id) ?? `Pokemon #${id}`;
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
  /**
   * Indexes the grunt masterdata by `grunt_type` and gender, keeping only the entries that identify
   * exactly one grunt.
   *
   * A pair that matches two entries is dropped on purpose rather than resolved by picking the first.
   * `giovanni` and `arlo` each have a ticketed and an unticketed entry at gender 0, and a typed grunt
   * like `dark` has no gender-0 entry at all while a rule may well be set to "any gender" -- naming
   * either of those after one of its halves would tell the user their rule is narrower than it is.
   * Those cases fall through to the hand-maintained label, which is what they already show.
   *
   * `short_name` rather than `name`: it is the form built for a label ("Unlicht ♀" against
   * "Unlicht - Rüpel (Weiblich)"), and unlike `type` it is translated for every grunt rather than
   * only the eighteen elemental ones.
   */
  private applyGrunts(grunts: null | Record<string, GruntEntry>): void {
    const names = new Map<string, string>();

    if (grunts) {
      const seen = new Map<string, number>();

      Object.values(grunts).forEach(entry => {
        const gruntType = entry?.grunt_type;
        const shortName = entry?.short_name;
        if (!gruntType || !shortName) return;

        const key = `${gruntType}:${entry.gender ?? 0}`;
        seen.set(key, (seen.get(key) ?? 0) + 1);
        names.set(key, shortName);
      });

      seen.forEach((count, key) => {
        if (count > 1) names.delete(key);
      });
    }

    this.gruntMap.set(names);
  }

  private applyMonsters(monsters: null | Record<string, MonsterEntry>, names: Map<number, string>): void {
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

    namesById.forEach((name, id) => names.set(id, name));
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
      // Costume names are English at source (no upstream translated list exists), so unlike monsters
      // they are not refetched on a language change. A failure degrades to "names unavailable" rather
      // than blocking ready$ -- the two sentinel choices work without them.
      costumes: this.http
        .get<Record<string, string>>(`${this.config.apiHost}/api/masterdata/costumes`)
        .pipe(catchError(() => of({} as Record<string, string>))),
      // Translated, so refetched on a language change like monsters. A PoracleNG too old to carry the
      // names answers its older shape and this produces nothing, which leaves every label exactly where
      // it is today -- the hand-maintained strings. See #840.
      grunts: this.http
        .get<Record<string, GruntEntry>>(`${this.config.apiHost}/api/masterdata/grunts`, { params: { locale } })
        .pipe(catchError(() => of(null))),
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
      next: ({ costumes, grunts, items, monsters, moves, pokemon }) => {
        // Each map is rebuilt whole and published once. Mutating the live map in place would not
        // notify anything reading it, and would briefly show a half-filled list to anything that
        // did.
        const pokemonNames = new Map<number, string>();
        if (pokemon) {
          Object.entries(pokemon).forEach(([id, name]) => {
            pokemonNames.set(Number(id), name as string);
          });
        }

        const itemNames = new Map<number, string>();
        if (items) {
          Object.entries(items).forEach(([id, name]) => {
            itemNames.set(Number(id), name as string);
          });
        }

        const costumeNames = new Map<number, string>();
        if (costumes) {
          Object.entries(costumes).forEach(([id, name]) => {
            costumeNames.set(Number(id), name as string);
          });
        }

        const moveNames = new Map<number, string>();
        if (moves) {
          Object.entries(moves).forEach(([id, name]) => {
            moveNames.set(Number(id), name as string);
          });
        }

        // Translated species names overwrite the English ones, so this runs before publishing.
        this.applyMonsters(monsters, pokemonNames);
        this.applyGrunts(grunts);

        this.itemMap.set(itemNames);
        this.costumeMap.set(costumeNames);
        this.moveMap.set(moveNames);
        this.pokemonMap.set(pokemonNames);

        this.loaded = true;
        this.ready$.next(true);
      },
    });
  }
}
