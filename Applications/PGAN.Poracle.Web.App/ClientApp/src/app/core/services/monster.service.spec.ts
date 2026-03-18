import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { MonsterService } from './monster.service';
import { Monster, MonsterCreate, MonsterUpdate } from '../models';

describe('MonsterService', () => {
  let service: MonsterService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockMonster: Monster = {
    atk: 15, clean: 0, def: 14, distance: 0, form: 0, gender: 0,
    id: '1', maxAtk: 15, maxCp: 9999, maxDef: 15, maxIv: 100,
    maxLevel: 50, maxSta: 15, maxWeight: 9999, minCp: 0, minIv: 90,
    minLevel: 0, minWeight: 0, ping: null, pokemonId: 25, profileNo: 1,
    pvpRankingBest: 0, pvpRankingLeague: 0, pvpRankingMinCp: 0,
    pvpRankingWorst: 0, sta: 15, template: null, uid: 1,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(MonsterService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all monsters with GET', () => {
    service.getAll().subscribe(monsters => {
      expect(monsters).toHaveLength(2);
      expect(monsters[0].pokemonId).toBe(25);
    });

    const req = httpMock.expectOne(`${API}/api/monsters`);
    expect(req.request.method).toBe('GET');
    req.flush([mockMonster, { ...mockMonster, uid: 2, pokemonId: 150 }]);
  });

  it('should create a monster with POST', () => {
    const createPayload: MonsterCreate = {
      atk: 15, clean: 0, def: 14, distance: 0, form: 0, gender: 0,
      maxAtk: 15, maxCp: 9999, maxDef: 15, maxIv: 100, maxLevel: 50,
      maxSta: 15, maxWeight: 9999, minCp: 0, minIv: 90, minLevel: 0,
      minWeight: 0, ping: null, pokemonId: 25, profileNo: 1,
      pvpRankingBest: 0, pvpRankingLeague: 0, pvpRankingMinCp: 0,
      pvpRankingWorst: 0, sta: 15, template: null,
    };

    service.create(createPayload).subscribe(result => {
      expect(result.uid).toBe(1);
      expect(result.pokemonId).toBe(25);
    });

    const req = httpMock.expectOne(`${API}/api/monsters`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(createPayload);
    req.flush(mockMonster);
  });

  it('should update a monster with PUT', () => {
    const updatePayload: MonsterUpdate = { minIv: 95, distance: 500 };

    service.update(1, updatePayload).subscribe();

    const req = httpMock.expectOne(`${API}/api/monsters/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(updatePayload);
    req.flush(null);
  });

  it('should delete a single monster with DELETE', () => {
    service.delete(42).subscribe();

    const req = httpMock.expectOne(`${API}/api/monsters/42`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all monsters with DELETE', () => {
    service.deleteAll().subscribe();

    const req = httpMock.expectOne(`${API}/api/monsters`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances with PUT', () => {
    service.updateAllDistance(5000).subscribe();

    const req = httpMock.expectOne(`${API}/api/monsters/distance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ distance: 5000 });
    req.flush(null);
  });
});
