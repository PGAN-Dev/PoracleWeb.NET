import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { ProfileService } from './profile.service';
import { ActiveHourEntry, Profile } from '../models';

describe('ProfileService', () => {
  let service: ProfileService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockProfile: Profile = {
    active: true,
    activeHours: null,
    latitude: 0,
    longitude: 0,
    name: 'Default',
    profileNo: 1,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(ProfileService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all profiles', () => {
    service.getAll().subscribe(profiles => {
      expect(profiles).toHaveLength(2);
      expect(profiles[0].active).toBe(true);
    });

    httpMock
      .expectOne(`${API}/api/profiles`)
      .flush([
        { ...mockProfile, activeHours: null },
        { name: 'PVP', active: false, activeHours: null, latitude: 0, longitude: 0, profileNo: 2 },
      ]);
  });

  it('should create a profile', () => {
    service.create({ name: 'New Profile' }).subscribe(result => {
      expect(result.profileNo).toBe(3);
    });

    const req = httpMock.expectOne(`${API}/api/profiles`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'New Profile' });
    req.flush({ ...mockProfile, name: 'New Profile', active: false, profileNo: 3 });
  });

  it('should delete a profile', () => {
    service.delete(2).subscribe();

    const req = httpMock.expectOne(`${API}/api/profiles/2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should switch to a different profile', () => {
    service.switchProfile(2).subscribe(result => {
      expect(result.profile.active).toBe(true);
      expect(result.profile.profileNo).toBe(2);
      expect(result.token).toBe('new-token');
    });

    const req = httpMock.expectOne(`${API}/api/profiles/switch/2`);
    expect(req.request.method).toBe('PUT');
    req.flush({
      profile: { ...mockProfile, name: 'PVP', active: true, profileNo: 2 },
      token: 'new-token',
    });
  });

  it('should update a profile name', () => {
    service.update(1, 'Renamed').subscribe(result => {
      expect(result.name).toBe('Renamed');
    });

    const req = httpMock.expectOne(`${API}/api/profiles/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Renamed' });
    req.flush({ ...mockProfile, name: 'Renamed' });
  });

  it('should duplicate a profile', () => {
    service.duplicate(1, 'Copy of Default').subscribe(result => {
      expect(result.profileNo).toBe(2);
    });

    const req = httpMock.expectOne(`${API}/api/profiles/duplicate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Copy of Default', fromProfileNo: 1 });
    req.flush({ ...mockProfile, name: 'Copy of Default', active: false, profileNo: 2 });
  });

  it('should parse activeHours JSON string in profile list response', () => {
    const activeHoursJson = '[{"day":1,"hours":9,"mins":0}]';
    service.getAll().subscribe(profiles => {
      expect(profiles[0].activeHours).toEqual([{ day: 1, hours: 9, mins: 0 }]);
      expect(profiles[1].activeHours).toEqual([]);
    });

    httpMock.expectOne(`${API}/api/profiles`).flush([
      { ...mockProfile, activeHours: activeHoursJson },
      { ...mockProfile, name: 'PVP', profileNo: 2, activeHours: null },
    ]);
  });

  it('should handle undefined activeHours in profile response', () => {
    service.getAll().subscribe(profiles => {
      expect(profiles[0].activeHours).toEqual([]);
    });

    httpMock.expectOne(`${API}/api/profiles`).flush([{ ...mockProfile }]);
  });

  it('should call updateActiveHours endpoint with serialized entries', () => {
    const entries: ActiveHourEntry[] = [{ day: 1, hours: 9, mins: 0 }];
    service.updateActiveHours(1, entries).subscribe();

    const req = httpMock.expectOne(`${API}/api/profiles/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ activeHours: JSON.stringify(entries) });
    req.flush(null);
  });

  it('should call updateActiveHours with null', () => {
    service.updateActiveHours(1, null).subscribe();

    const req = httpMock.expectOne(`${API}/api/profiles/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ activeHours: null });
    req.flush(null);
  });
});
