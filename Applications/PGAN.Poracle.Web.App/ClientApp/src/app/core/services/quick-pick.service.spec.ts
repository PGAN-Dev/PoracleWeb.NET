import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { QuickPickService } from './quick-pick.service';

describe('QuickPickService', () => {
  let service: QuickPickService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(QuickPickService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch all quick picks via GET', () => {
    const picks = [
      {
        appliedState: null,
        definition: {
          id: '1',
          name: 'PvP Monsters',
          alarmType: 'monster',
          category: 'PvP',
          description: '',
          enabled: true,
          filters: {},
          icon: '',
          scope: 'global',
          sortOrder: 1,
        },
      },
    ];

    service.getAll().subscribe(result => {
      expect(result).toHaveLength(1);
      expect(result[0].definition.name).toBe('PvP Monsters');
    });

    const req = httpMock.expectOne(`${API}/api/quick-picks`);
    expect(req.request.method).toBe('GET');
    req.flush(picks);
  });

  it('should apply a quick pick via POST', () => {
    const request = { clean: 0, distance: 0, excludePokemonIds: [] };

    service.apply('abc', request).subscribe(result => {
      expect(result.quickPickId).toBe('abc');
    });

    const req = httpMock.expectOne(`${API}/api/quick-picks/abc/apply`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ trackedUids: [1, 2], appliedAt: '', excludeGruntTypes: [], excludeLureIds: [], excludePokemonIds: [], quickPickId: 'abc' });
  });

  it('should reapply a quick pick via POST', () => {
    const request = { clean: 1, distance: 5000 };

    service.reapply('xyz', request).subscribe(result => {
      expect(result.trackedUids).toHaveLength(3);
    });

    const req = httpMock.expectOne(`${API}/api/quick-picks/xyz/reapply`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({
      trackedUids: [1, 2, 3],
      appliedAt: '',
      excludeGruntTypes: [],
      excludeLureIds: [],
      excludePokemonIds: [],
      quickPickId: 'xyz',
    });
  });

  it('should remove a quick pick via DELETE', () => {
    service.remove('abc').subscribe();

    const req = httpMock.expectOne(`${API}/api/quick-picks/abc/remove`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should get a quick pick by id via GET', () => {
    service.getById('abc').subscribe(result => {
      expect(result.id).toBe('abc');
    });

    const req = httpMock.expectOne(`${API}/api/quick-picks/abc`);
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 'abc',
      name: 'Test',
      alarmType: 'monster',
      category: 'PvP',
      description: '',
      enabled: true,
      filters: {},
      icon: '',
      scope: 'global',
      sortOrder: 1,
    });
  });

  it('should delete an admin quick pick via DELETE', () => {
    service.deleteAdmin('abc').subscribe();

    const req = httpMock.expectOne(`${API}/api/quick-picks/abc`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete a user quick pick via DELETE', () => {
    service.deleteUser('abc').subscribe();

    const req = httpMock.expectOne(`${API}/api/quick-picks/user/abc`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should seed quick picks via POST', () => {
    service.seed().subscribe();

    const req = httpMock.expectOne(`${API}/api/quick-picks/seed`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });
});
