import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
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

      const pokemonReq = httpMock.expectOne(`${API}/api/masterdata/pokemon`);
      const itemsReq = httpMock.expectOne(`${API}/api/masterdata/items`);

      pokemonReq.flush({ '25': 'Pikachu', '150': 'Mewtwo' });
      itemsReq.flush({ '1': 'Poke Ball', '2': 'Great Ball' });

      // Also handle the forms request from loadForms()
      const formsReq = httpMock.expectOne(req => req.url.includes('master-latest-poracle'));
      formsReq.flush({});

      expect(service.isLoaded()).toBe(true);
      expect(service.getPokemonName(25)).toBe('Pikachu');
      expect(service.getPokemonName(150)).toBe('Mewtwo');
      expect(service.getItemName(1)).toBe('Poke Ball');
    });

    it('should only make one HTTP request even when called multiple times', () => {
      service.loadData().subscribe();
      service.loadData().subscribe();

      // Should only have one of each request
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '25': 'Pikachu' });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(req => req.url.includes('master-latest-poracle')).flush({});
    });

    it('should handle API errors gracefully', () => {
      service.loadData().subscribe(ready => {
        expect(ready).toBe(true);
      });

      // forkJoin cancels remaining requests when one errors, so only error the first
      httpMock.expectOne(`${API}/api/masterdata/pokemon`).error(
        new ProgressEvent('error'), { status: 500, statusText: 'Error' },
      );
      // The items request gets cancelled by forkJoin, so just match and discard it
      httpMock.match(`${API}/api/masterdata/items`);

      expect(service.isLoaded()).toBe(true);
    });
  });

  describe('getAllPokemon', () => {
    it('should return sorted list with "All Pokemon" entry at start', () => {
      service.loadData().subscribe();

      httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({
        '150': 'Mewtwo', '25': 'Pikachu', '1': 'Bulbasaur',
      });
      httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
      httpMock.expectOne(req => req.url.includes('master-latest-poracle')).flush({});

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
  });
});
