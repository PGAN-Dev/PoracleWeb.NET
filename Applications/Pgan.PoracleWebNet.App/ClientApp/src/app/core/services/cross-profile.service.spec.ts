import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CrossProfileOverview } from '../models';
import { ConfigService } from './config.service';
import { CrossProfileService } from './cross-profile.service';

describe('CrossProfileService', () => {
  let service: CrossProfileService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockOverview: CrossProfileOverview = {
    raid: [{ raid_pokemon_id: 150, uid: 4, exclusive: 0, level: 5, profile_no: 1 }],
    egg: [{ uid: 1, distance: 0, level: 5, profile_no: 1 }],
    gym: [],
    invasion: [],
    lure: [],
    nest: [],
    pokemon: [
      { pokemon_id: 25, uid: 2, max_iv: 100, min_iv: 90, profile_no: 1 },
      { pokemon_id: 150, uid: 3, max_iv: 100, min_iv: 0, profile_no: 2 },
    ],
    profile: [
      { id: 'abc', name: 'Default', profile_no: 1 },
      { id: 'def', name: 'PvP', profile_no: 2 },
    ],
    quest: [],
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(CrossProfileService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch overview with GET /api/cross-profile', () => {
    service.getOverview().subscribe();

    const req = httpMock.expectOne(`${API}/api/cross-profile`);
    expect(req.request.method).toBe('GET');
    req.flush(mockOverview);
  });

  it('should return CrossProfileOverview shape', () => {
    service.getOverview().subscribe(overview => {
      expect(overview.profile).toHaveLength(2);
      expect(overview.pokemon).toHaveLength(2);
      expect(overview.raid).toHaveLength(1);
      expect(overview.egg).toHaveLength(1);
      expect(overview.gym).toHaveLength(0);
      expect(overview.invasion).toHaveLength(0);
      expect(overview.lure).toHaveLength(0);
      expect(overview.nest).toHaveLength(0);
      expect(overview.quest).toHaveLength(0);
      expect(overview.pokemon[0].pokemon_id).toBe(25);
      expect(overview.pokemon[1].profile_no).toBe(2);
      expect(overview.profile[0].name).toBe('Default');
    });

    const req = httpMock.expectOne(`${API}/api/cross-profile`);
    expect(req.request.method).toBe('GET');
    req.flush(mockOverview);
  });
});
