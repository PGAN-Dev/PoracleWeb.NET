import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { EggService } from './egg.service';
import { Egg, EggCreate } from '../models';

describe('EggService', () => {
  let service: EggService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockEgg: Egg = {
    clean: 0, distance: 0, exclusive: 0, id: '1',
    level: 5, ping: null, profileNo: 1, team: 0,
    template: null, uid: 1,
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
    service = TestBed.inject(EggService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all eggs', () => {
    service.getAll().subscribe(eggs => {
      expect(eggs).toHaveLength(1);
      expect(eggs[0].level).toBe(5);
    });
    httpMock.expectOne(`${API}/api/eggs`).flush([mockEgg]);
  });

  it('should create an egg', () => {
    const payload: EggCreate = {
      clean: 0, distance: 0, exclusive: 1, level: 3,
      ping: null, profileNo: 1, team: 0, template: null,
    };
    service.create(payload).subscribe(r => expect(r.uid).toBe(1));
    const req = httpMock.expectOne(`${API}/api/eggs`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.exclusive).toBe(1);
    req.flush(mockEgg);
  });

  it('should update an egg', () => {
    service.update(1, { level: 3 }).subscribe();
    const req = httpMock.expectOne(`${API}/api/eggs/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete an egg', () => {
    service.delete(1).subscribe();
    httpMock.expectOne(`${API}/api/eggs/1`).flush(null);
  });

  it('should delete all eggs', () => {
    service.deleteAll().subscribe();
    httpMock.expectOne(`${API}/api/eggs`).flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(800).subscribe();
    const req = httpMock.expectOne(`${API}/api/eggs/distance`);
    expect(req.request.body).toBe(800);
    req.flush(null);
  });
});
