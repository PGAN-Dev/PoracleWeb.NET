import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { GymService } from './gym.service';
import { Gym, GymCreate } from '../models';

describe('GymService', () => {
  let service: GymService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockGym: Gym = {
    id: '1',
    uid: 1,
    battle_changes: 0,
    clean: 0,
    distance: 0,
    gymId: null,
    ping: null,
    profileNo: 1,
    slot_changes: 0,
    team: 1,
    template: null,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(GymService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all gyms', () => {
    service.getAll().subscribe(gyms => {
      expect(gyms).toHaveLength(1);
      expect(gyms[0].team).toBe(1);
    });
    httpMock.expectOne(`${API}/api/gyms`).flush([mockGym]);
  });

  it('should create a gym', () => {
    const payload: GymCreate = {
      battle_changes: 1,
      clean: 0,
      distance: 0,
      gymId: null,
      ping: null,
      profileNo: 1,
      slot_changes: 1,
      team: 2,
      template: null,
    };
    service.create(payload).subscribe(r => expect(r.uid).toBe(1));
    const req = httpMock.expectOne(`${API}/api/gyms`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.team).toBe(2);
    req.flush(mockGym);
  });

  it('should update a gym', () => {
    service.update(1, { team: 3 }).subscribe();
    const req = httpMock.expectOne(`${API}/api/gyms/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete a gym', () => {
    service.delete(1).subscribe();
    httpMock.expectOne(`${API}/api/gyms/1`).flush(null);
  });

  it('should delete all gyms', () => {
    service.deleteAll().subscribe();
    httpMock.expectOne(`${API}/api/gyms`).flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(1500).subscribe();
    const req = httpMock.expectOne(`${API}/api/gyms/distance`);
    expect(req.request.body).toBe(1500);
    req.flush(null);
  });
});
