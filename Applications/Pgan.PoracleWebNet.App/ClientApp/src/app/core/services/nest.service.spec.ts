import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { NestService } from './nest.service';
import { Nest, NestCreate } from '../models';

describe('NestService', () => {
  let service: NestService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockNest: Nest = {
    id: '1',
    uid: 1,
    clean: 0,
    distance: 0,
    minSpawnAvg: 5,
    ping: null,
    pokemonId: 25,
    profileNo: 1,
    template: null,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(NestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all nests', () => {
    service.getAll().subscribe(nests => {
      expect(nests).toHaveLength(1);
      expect(nests[0].minSpawnAvg).toBe(5);
    });
    httpMock.expectOne(`${API}/api/nests`).flush([mockNest]);
  });

  it('should create a nest', () => {
    const payload: NestCreate = {
      clean: 0,
      distance: 0,
      minSpawnAvg: 10,
      ping: null,
      pokemonId: 25,
      profileNo: 1,
      template: null,
    };
    service.create(payload).subscribe(r => expect(r.uid).toBe(1));
    const req = httpMock.expectOne(`${API}/api/nests`);
    expect(req.request.method).toBe('POST');
    req.flush(mockNest);
  });

  it('should update a nest', () => {
    service.update(1, { minSpawnAvg: 15 }).subscribe();
    const req = httpMock.expectOne(`${API}/api/nests/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete a nest', () => {
    service.delete(1).subscribe();
    httpMock.expectOne(`${API}/api/nests/1`).flush(null);
  });

  it('should delete all nests', () => {
    service.deleteAll().subscribe();
    httpMock.expectOne(`${API}/api/nests`).flush(null);
  });

  it('sends several alarms to the batch endpoint as one request (#1090)', () => {
    const rows = [{ pokemonId: 1 }, { pokemonId: 4 }] as unknown as NestCreate[];
    service.createBatch(rows).subscribe(res => expect(res.alarms.map(a => a.uid)).toEqual([7, 0]));

    const req = httpMock.expectOne(`${API}/api/nests/batch`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(rows);
    req.flush({ alarms: [{ uid: 7 }, { uid: 0 }], takeoversSkipped: 0 });
  });
});
