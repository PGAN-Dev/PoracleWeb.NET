import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { ProfileService } from './profile.service';
import { Profile } from '../models';

describe('ProfileService', () => {
  let service: ProfileService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockProfile: Profile = { active: true, name: 'Default', profileNo: 1 };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
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

    httpMock.expectOne(`${API}/api/profiles`).flush([
      mockProfile,
      { active: false, name: 'PVP', profileNo: 2 },
    ]);
  });

  it('should create a profile', () => {
    service.create({ name: 'New Profile' }).subscribe(result => {
      expect(result.profileNo).toBe(3);
    });

    const req = httpMock.expectOne(`${API}/api/profiles`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'New Profile' });
    req.flush({ active: false, name: 'New Profile', profileNo: 3 });
  });

  it('should delete a profile', () => {
    service.delete(2).subscribe();

    const req = httpMock.expectOne(`${API}/api/profiles/2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should switch to a different profile', () => {
    service.switchProfile(2).subscribe(result => {
      expect(result.active).toBe(true);
      expect(result.profileNo).toBe(2);
    });

    const req = httpMock.expectOne(`${API}/api/profiles/switch/2`);
    expect(req.request.method).toBe('PUT');
    req.flush({ active: true, name: 'PVP', profileNo: 2 });
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

  it('should copy alarms between profiles', () => {
    service.copy(1, 2).subscribe();

    const req = httpMock.expectOne(`${API}/api/profiles/copy`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ fromProfile: 1, toProfile: 2 });
    req.flush(null);
  });
});
