import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { RaidLevelService } from './raid-level.service';
import { KNOWN_LEVELS } from '../models/raid-level.models';

describe('RaidLevelService', () => {
  let service: RaidLevelService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RaidLevelService);
    http = TestBed.inject(HttpTestingController);
  });

  it('starts with the baked-in 19 levels before any fetch', () => {
    expect(service.levels().length).toBe(KNOWN_LEVELS.length);
    expect(service.levels()[0].value).toBe(1);
    expect(service.loaded()).toBe(false);
  });

  it('replaces the list with the API payload on successful load', () => {
    service.load();
    const req = http.expectOne(r => r.url.endsWith('/api/masterdata/raid-levels'));
    req.flush([
      { name: '1 Star Raid', namePlural: '1 Star Raids', category: 'star', value: 1 },
      { name: 'Future Raid', namePlural: 'Future Raids', category: 'special', value: 20 },
    ]);

    expect(service.loaded()).toBe(true);
    expect(service.levels().length).toBe(2);
    expect(service.levels()[1].value).toBe(20);
    expect(service.levels()[1].labelKey).toBe('RAIDS.LEVEL.RAID_20');
  });

  it('keeps the baked-in fallback when the API fails', () => {
    service.load();
    const req = http.expectOne(r => r.url.endsWith('/api/masterdata/raid-levels'));
    req.error(new ProgressEvent('network'), { status: 500, statusText: 'Server Error' });

    expect(service.loaded()).toBe(true);
    expect(service.levels().length).toBe(KNOWN_LEVELS.length);
  });

  it('keeps the baked-in fallback when the API returns an empty list', () => {
    service.load();
    const req = http.expectOne(r => r.url.endsWith('/api/masterdata/raid-levels'));
    req.flush([]);

    expect(service.loaded()).toBe(true);
    expect(service.levels().length).toBe(KNOWN_LEVELS.length);
  });

  it('coerces unknown category strings to "custom"', () => {
    service.load();
    const req = http.expectOne(r => r.url.endsWith('/api/masterdata/raid-levels'));
    req.flush([{ name: 'Whatever', namePlural: 'Whatevers', category: 'invented-category', value: 99 }]);

    expect(service.levels()[0].category).toBe('custom');
  });

  it('subsequent load() calls are no-ops once loaded', () => {
    service.load();
    const req = http.expectOne(r => r.url.endsWith('/api/masterdata/raid-levels'));
    req.flush([{ name: 'Legendary Raid', namePlural: 'Legendary Raids', category: 'star', value: 5 }]);

    service.load(); // second call should not issue another HTTP request
    http.expectNone(r => r.url.endsWith('/api/masterdata/raid-levels'));
  });

  it('byValue map exposes a lookup keyed by value', () => {
    expect(service.byValue().get(7)?.labelKey).toBe('RAIDS.LEVEL.RAID_7');
    expect(service.byValue().get(9000)).toBeUndefined();
  });

  afterEach(() => http.verify());
});
