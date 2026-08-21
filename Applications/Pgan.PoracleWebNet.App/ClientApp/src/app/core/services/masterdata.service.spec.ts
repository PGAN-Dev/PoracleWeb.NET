import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { MasterDataService } from './masterdata.service';

describe('MasterDataService', () => {
  let service: MasterDataService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(MasterDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('before loading data', () => {
    it('should not be loaded initially', () => {
      expect(service.isLoaded()).toBe(false);
    });

    it('should return fallback for unknown pokemon ID', () => {
      expect(service.getPokemonName(9999)).toBe('Pokemon #9999');
    });

    it('should return "All Pokemon" for ID 0', () => {
      expect(service.getPokemonName(0)).toBe('All Pokemon');
    });

    it('should return fallback for unknown item ID', () => {
      expect(service.getItemName(42)).toBe('Item #42');
    });

    it('should return ID itself for unknown evolution base', () => {
      expect(service.getBaseEvolution(999)).toBe(999);
    });
  });

  describe('loadData', () => {
    it('should load pokemon and item data', () => {
      service.loadData().subscribe(ready => {
        expect(ready).toBe(true);
      });

      const monstersReq = httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`);
      const pokemonReq = httpMock.expectOne(`${API}/api/masterdata/pokemon`);
      const itemsReq = httpMock.expectOne(`${API}/api/masterdata/items`);
      const movesReq = httpMock.expectOne(`${API}/api/masterdata/moves`);

      pokemonReq.flush({ '25': 'Pikachu', '150': 'Mewtwo' });
      itemsReq.flush({ '1': 'Poke Ball', '2': 'Great Ball' });
      movesReq.flush({ '13': 'Wrap', '14': 'Hyper Beam' });
      monstersReq.flush({});

      expect(service.isLoaded()).toBe(true);
      expect(service.getPokemonName(25)).toBe('Pikachu');
      expect(service.getPokemonName(150)).toBe('Mewtwo');
      expect(service.getItemName(1)).toBe('Poke Ball');
      expect(service.getMoveName(13)).toBe('Wrap');
      expect(service.getMoveName(14)).toBe('Hyper Beam');
    });

    it('should fall back to "Move #id" for an unknown move (#396)', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({ '13': 'Wrap' });
      httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});

      expect(service.getMoveName(13)).toBe('Wrap');
      expect(service.getMoveName(9999)).toBe('Move #9999');
    });

    it('should only make one HTTP request even when called multiple times', () => {
      service.loadData().subscribe();
      service.loadData().subscribe();

      // Should only have one of each request
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '25': 'Pikachu' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});
    });

    it('should handle API errors gracefully', () => {
      service.loadData().subscribe(ready => {
        expect(ready).toBe(true);
      });

      // forkJoin cancels remaining requests when one errors, so only error the first
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
      // The items request gets cancelled by forkJoin, so just match and discard it
      httpMock.match(`${API}/api/masterdata/items`);
      httpMock.match(`${API}/api/masterdata/moves`);
      httpMock.match(req => req.url === `${API}/api/masterdata/monsters`);

      expect(service.isLoaded()).toBe(true);
    });
  });

  describe('getAllPokemon', () => {
    it('should return sorted list with "All Pokemon" entry at start', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({
        '1': 'Bulbasaur',
        '25': 'Pikachu',
        '150': 'Mewtwo',
      });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});

      const pokemon = service.getAllPokemon();
      expect(pokemon[0]).toEqual({ id: 0, name: 'All Pokemon' });
      expect(pokemon[1]).toEqual({ id: 1, name: 'Bulbasaur' });
      expect(pokemon[2]).toEqual({ id: 25, name: 'Pikachu' });
      expect(pokemon[3]).toEqual({ id: 150, name: 'Mewtwo' });
    });
  });

  describe('getFormName', () => {
    it('should return empty string for form ID 0', () => {
      expect(service.getFormName(25, 0)).toBe('');
    });

    it('should return fallback for unknown form', () => {
      expect(service.getFormName(25, 999)).toBe('Form 999');
    });
  });

  describe('getFormsForPokemon', () => {
    it('should return empty array for pokemon with no forms', () => {
      expect(service.getFormsForPokemon(1)).toEqual([]);
    });

    it('should keep the base "Normal" form when a regional variant exists', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '618': 'Stunfisk' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock
        .expectOne(req => req.url === `${API}/api/masterdata/monsters`)
        .flush({
          '618_0': { id: 618, name: 'Stunfisk', form: { id: 0, name: '' } },
          '618_2246': { id: 618, name: 'Stunfisk', form: { id: 2246, name: 'Normal' } },
          '618_2345': { id: 618, name: 'Stunfisk', form: { id: 2345, name: 'Galarian' } },
        });

      expect(service.getFormsForPokemon(618)).toEqual([
        { id: 2345, name: 'Galarian' },
        { id: 2246, name: 'Normal' },
      ]);
    });

    it('should drop a lone base form under a translated name (it: "Normale")', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '1': 'Bulbasaur' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock
        .expectOne(req => req.url === `${API}/api/masterdata/monsters`)
        .flush({
          '1_0': { id: 1, name: 'Bulbasaur', form: { id: 0, name: '' } },
          '1_123': { id: 1, name: 'Bulbasaur', form: { id: 123, name: 'Normale' } },
        });

      expect(service.getFormsForPokemon(1)).toEqual([]);
    });

    // Koraidon and Miraidon are the only two species in live data whose single real form is not
    // the base one, so the drop rule has to be about the name and not about the count.
    it('should keep a lone form that is not the base form', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '1007': 'Koraidon' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock
        .expectOne(req => req.url === `${API}/api/masterdata/monsters`)
        .flush({
          '1007_0': { id: 1007, name: 'Koraidon', form: { id: 0, name: '' } },
          '1007_3084': { id: 1007, name: 'Koraidon', form: { id: 3084, name: 'Apex Build' } },
        });

      expect(service.getFormsForPokemon(1007)).toEqual([{ id: 3084, name: 'Apex Build' }]);
    });

    it('should drop a lone "Normal" form covered by "All Forms"', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '1': 'Bulbasaur' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock
        .expectOne(req => req.url === `${API}/api/masterdata/monsters`)
        .flush({
          '1_0': { id: 1, name: 'Bulbasaur', form: { id: 0, name: '' } },
          '1_123': { id: 1, name: 'Bulbasaur', form: { id: 123, name: 'Normal' } },
        });

      expect(service.getFormsForPokemon(1)).toEqual([]);
    });
  });
  describe('localized monster data', () => {
    const MONSTERS = `${API}/api/masterdata/monsters`;

    /** Flushes the three English maps, then the monster map, and returns nothing. */
    function load(monsters: unknown, pokemon: Record<string, string> = {}): void {
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush(pokemon);
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock.expectOne(req => req.url === MONSTERS).flush(monsters);
    }

    it('should request the monster map for the current display language', () => {
      TestBed.inject(I18nService).use('de');
      service.loadData().subscribe();

      const req = httpMock.expectOne(r => r.url === MONSTERS);
      expect(req.request.params.get('locale')).toBe('de');

      req.flush({});
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
    });

    it('should prefer the translated name over the English masterfile name', () => {
      service.loadData().subscribe();
      load({ '25_0': { id: 25, name: 'Pikachu', form: { id: 0, name: '' } } }, { '25': 'Pikachu' });
      expect(service.getPokemonName(25)).toBe('Pikachu');

      // Species with names that actually differ between locales, e.g. #001 in German.
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideTranslateService(),
          { provide: ConfigService, useValue: { apiHost: API } },
        ],
      });
      const german = TestBed.inject(MasterDataService);
      const germanHttp = TestBed.inject(HttpTestingController);
      german.loadData().subscribe();
      germanHttp.expectOne(`${API}/api/masterdata/pokemon`).flush({ '1': 'Bulbasaur' });
      germanHttp.expectOne(`${API}/api/masterdata/items`).flush({});
      germanHttp.expectOne(`${API}/api/masterdata/moves`).flush({});
      germanHttp.expectOne(req => req.url === MONSTERS).flush({ '1_0': { id: 1, name: 'Bisasam', form: { id: 0, name: '' } } });

      expect(german.getPokemonName(1)).toBe('Bisasam');
      germanHttp.verify();
    });

    it('should keep English type names as identity and translate only the label', () => {
      service.loadData().subscribe();
      load({
        '25_0': {
          id: 25,
          name: 'Pikachu',
          form: { id: 0, name: '' },
          types: [{ id: 13, name: 'Elektro' }],
        },
      });

      // Icons and the type filter chip both key on the English name, so it has to survive.
      expect(service.getPokemonTypes(25)).toEqual(['Electric']);
      expect(service.getAllTypes()).toEqual(['Electric']);
      expect(service.getTypeLabel('Electric')).toBe('Elektro');
    });

    it('should fall back to the English name when a translation key comes back untranslated', () => {
      service.loadData().subscribe();
      load(
        {
          '25_0': {
            id: 25,
            name: 'poke_25',
            form: { id: 0, name: '' },
            types: [{ id: 13, name: 'poke_type_13' }],
          },
        },
        { '25': 'Pikachu' },
      );

      expect(service.getPokemonName(25)).toBe('Pikachu');
      expect(service.getTypeLabel('Electric')).toBe('Electric');
    });

    it('should keep English names when the monster map is unavailable', () => {
      service.loadData().subscribe();
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '25': 'Pikachu' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
      httpMock.expectOne(req => req.url === MONSTERS).error(new ProgressEvent('error'), { status: 404, statusText: 'Not Found' });

      expect(service.isLoaded()).toBe(true);
      expect(service.getPokemonName(25)).toBe('Pikachu');
    });

    it('should reload and re-emit when the display language changes', () => {
      const seen: string[] = [];
      service.getAllPokemon$().subscribe(list => seen.push(list[1]?.name));
      load({ '25_0': { id: 25, name: 'Pikachu', form: { id: 0, name: '' } } }, { '25': 'Pikachu' });

      TestBed.inject(I18nService).use('fr');
      TestBed.flushEffects();

      const req = httpMock.expectOne(r => r.url === MONSTERS);
      expect(req.request.params.get('locale')).toBe('fr');
      req.flush({ '25_0': { id: 25, name: 'Pikachu (fr)', form: { id: 0, name: '' } } });
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '25': 'Pikachu' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});

      expect(seen).toEqual(['Pikachu', 'Pikachu (fr)']);
    });
  });
});
