import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { MaxBattleService } from './max-battle.service';
import { MaxBattle, MaxBattleCreate, MaxBattleUpdate } from '../models';

describe('MaxBattleService', () => {
  let service: MaxBattleService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockMaxBattle: MaxBattle = {
    id: '123456789',
    uid: 1,
    clean: 0,
    distance: 0,
    evolution: 9000,
    form: 0,
    gmax: 0,
    level: 3,
    move: 9000,
    ping: '',
    pokemonId: 150,
    profileNo: 1,
    stationId: null,
    template: '1',
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(MaxBattleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all max battles with GET', () => {
    service.getAll().subscribe(maxBattles => {
      expect(maxBattles).toHaveLength(1);
      expect(maxBattles[0].level).toBe(3);
    });

    const req = httpMock.expectOne(`${API}/api/maxbattles`);
    expect(req.request.method).toBe('GET');
    req.flush([mockMaxBattle]);
  });

  it('should create a max battle with POST', () => {
    const payload: MaxBattleCreate = {
      clean: 0,
      distance: 0,
      evolution: 9000,
      form: 0,
      gmax: 0,
      level: 3,
      move: 9000,
      ping: '',
      pokemonId: 150,
      stationId: null,
      template: '1',
    };

    service.create(payload).subscribe(result => {
      expect(result.uid).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/maxbattles`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.pokemonId).toBe(150);
    req.flush(mockMaxBattle);
  });

  it('should update a max battle with PUT', () => {
    const update: MaxBattleUpdate = { level: 5 };
    service.update(1, update).subscribe();

    const req = httpMock.expectOne(`${API}/api/maxbattles/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(update);
    req.flush(null);
  });

  it('should delete a max battle by uid with DELETE', () => {
    service.delete(5).subscribe();

    const req = httpMock.expectOne(`${API}/api/maxbattles/5`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all max battles with DELETE', () => {
    service.deleteAll().subscribe();

    const req = httpMock.expectOne(`${API}/api/maxbattles`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances with PUT', () => {
    service.updateAllDistance(3000).subscribe();

    const req = httpMock.expectOne(`${API}/api/maxbattles/distance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toBe(3000);
    req.flush(null);
  });

  it('should update bulk distances with PUT', () => {
    service.updateBulkDistance([1, 2], 500).subscribe();

    const req = httpMock.expectOne(`${API}/api/maxbattles/distance/bulk`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ uids: [1, 2], distance: 500 });
    req.flush(null);
  });
});
