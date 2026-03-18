import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { RaidService } from './raid.service';
import { Raid, RaidCreate, RaidUpdate } from '../models';

describe('RaidService', () => {
  let service: RaidService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockRaid: Raid = {
    clean: 0, distance: 0, exclusive: 0, form: 0, gymId: null,
    id: '1', level: 5, move: 0, ping: null, pokemonId: 150,
    profileNo: 1, team: 0, template: null, uid: 1,
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
    service = TestBed.inject(RaidService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all raids', () => {
    service.getAll().subscribe(raids => {
      expect(raids).toHaveLength(1);
      expect(raids[0].level).toBe(5);
    });

    const req = httpMock.expectOne(`${API}/api/raids`);
    expect(req.request.method).toBe('GET');
    req.flush([mockRaid]);
  });

  it('should create a raid', () => {
    const payload: RaidCreate = {
      clean: 0, distance: 0, exclusive: 1, form: 0, gymId: null,
      level: 5, move: 0, ping: null, pokemonId: 150, profileNo: 1,
      team: 0, template: 'default',
    };

    service.create(payload).subscribe(result => {
      expect(result.uid).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/raids`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.exclusive).toBe(1);
    req.flush(mockRaid);
  });

  it('should update a raid', () => {
    const update: RaidUpdate = { level: 3 };
    service.update(1, update).subscribe();

    const req = httpMock.expectOne(`${API}/api/raids/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(update);
    req.flush(null);
  });

  it('should delete a raid by uid', () => {
    service.delete(5).subscribe();

    const req = httpMock.expectOne(`${API}/api/raids/5`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all raids', () => {
    service.deleteAll().subscribe();

    const req = httpMock.expectOne(`${API}/api/raids`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(3000).subscribe();

    const req = httpMock.expectOne(`${API}/api/raids/distance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toBe(3000);
    req.flush(null);
  });
});
