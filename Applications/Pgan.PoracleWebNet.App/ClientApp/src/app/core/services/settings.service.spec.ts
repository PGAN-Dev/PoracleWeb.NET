import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { PwebSetting, SiteSetting } from '../models';

describe('SettingsService', () => {
  let service: SettingsService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockPwebSettings: PwebSetting[] = [
    { setting: 'enable_templates', value: 'true' },
    { setting: 'disable_raids', value: 'false' },
    { setting: 'site_name', value: 'My Site' },
  ];

  const mockSiteSettings: SiteSetting[] = [
    { id: 1, category: 'features', key: 'enable_templates', value: 'true', valueType: 'boolean' },
    { id: 2, category: 'features', key: 'disable_raids', value: 'false', valueType: 'boolean' },
    { id: 3, category: 'branding', key: 'site_name', value: 'My Site', valueType: 'string' },
  ];

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(SettingsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /**
   * getAll() fans out to two endpoints: the settings themselves and the disable_* keys Poracle
   * forces off upstream. Both have to be answered or httpMock.verify() reports the outstanding one.
   */
  const flushGetAll = (settings: unknown, upstream: string[] = []): void => {
    httpMock.expectOne(`${API}/api/settings`).flush(settings);
    httpMock.expectOne(`${API}/api/settings/upstream-disabled`).flush(upstream);
  };

  describe('normalize', () => {
    it('should normalize PwebSetting items', () => {
      const result = service.normalize(mockPwebSettings);
      expect(result['enable_templates']).toBe('true');
      expect(result['site_name']).toBe('My Site');
    });

    it('should normalize SiteSetting items', () => {
      const result = service.normalize(mockSiteSettings);
      expect(result['enable_templates']).toBe('true');
      expect(result['site_name']).toBe('My Site');
    });

    it('should handle mixed arrays', () => {
      const mixed = [mockPwebSettings[0], mockSiteSettings[2]];
      const result = service.normalize(mixed);
      expect(result['enable_templates']).toBe('true');
      expect(result['site_name']).toBe('My Site');
    });

    it('should handle null values by mapping to empty string', () => {
      const result = service.normalize([{ setting: 'key1', value: null }]);
      expect(result['key1']).toBe('');
    });
  });

  describe('getAll', () => {
    it('should fetch settings and populate siteSettings signal with PwebSetting response', () => {
      service.getAll().subscribe(settings => {
        expect(settings).toHaveLength(3);
      });

      flushGetAll(mockPwebSettings);

      expect(service.siteSettings()['enable_templates']).toBe('true');
      expect(service.siteSettings()['site_name']).toBe('My Site');
    });

    it('should fetch settings and populate siteSettings signal with SiteSetting response', () => {
      service.getAll().subscribe(settings => {
        expect(settings).toHaveLength(3);
      });

      flushGetAll(mockSiteSettings);

      expect(service.siteSettings()['enable_templates']).toBe('true');
      expect(service.siteSettings()['site_name']).toBe('My Site');
    });

    it('should update siteSettings on every call', () => {
      // First call
      service.getAll().subscribe();
      flushGetAll(mockSiteSettings);

      expect(service.siteSettings()['enable_templates']).toBe('true');

      // Second call - signal should update with new data
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'enable_templates', value: 'false', valueType: 'boolean' }]);

      expect(service.siteSettings()['enable_templates']).toBe('false');
    });

    it('should handle settings with null values', () => {
      service.getAll().subscribe();

      flushGetAll([{ id: 1, category: 'test', key: 'key1', value: null, valueType: 'string' }]);

      expect(service.siteSettings()['key1']).toBe('');
    });
  });

  describe('isDisabled', () => {
    it('should return true when setting is "true"', () => {
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'disable_raids', value: 'true', valueType: 'boolean' }]);

      expect(service.isDisabled('disable_raids')).toBe(true);
    });

    it('should return true case-insensitively', () => {
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'disable_raids', value: 'True', valueType: 'boolean' }]);

      expect(service.isDisabled('disable_raids')).toBe(true);
    });

    it('should return false when setting is not "true"', () => {
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'disable_raids', value: 'false', valueType: 'boolean' }]);

      expect(service.isDisabled('disable_raids')).toBe(false);
    });

    it('should return false for non-existent settings', () => {
      expect(service.isDisabled('nonexistent')).toBe(false);
    });
  });

  describe('upstream Poracle disable flags (#769)', () => {
    it('leaves every type enabled when Poracle disables nothing, which is what prod serves', () => {
      service.getAll().subscribe();
      flushGetAll(mockSiteSettings, []);

      for (const key of [
        'disable_mons',
        'disable_raids',
        'disable_quests',
        'disable_invasions',
        'disable_lures',
        'disable_nests',
        'disable_gyms',
        'disable_maxbattles',
        'disable_fort_changes',
      ]) {
        expect(service.isDisabled(key)).toBe(false);
        expect(service.isForcedByPoracle(key)).toBe(false);
      }
    });

    it('reports a type as disabled when Poracle forces it off and the site setting does not', () => {
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'disable_raids', value: 'false', valueType: 'boolean' }], ['disable_raids']);

      expect(service.isDisabled('disable_raids')).toBe(true);
      expect(service.isForcedByPoracle('disable_raids')).toBe(true);
    });

    it('does not touch the keys Poracle has no opinion about', () => {
      service.getAll().subscribe();
      flushGetAll(mockSiteSettings, ['disable_raids']);

      expect(service.isDisabled('disable_areas')).toBe(false);
      expect(service.isDisabled('disable_profiles')).toBe(false);
      expect(service.isForcedByPoracle('disable_quests')).toBe(false);
    });

    it('leaves the site settings in sole charge when the upstream call fails', () => {
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush(mockSiteSettings);
      httpMock.expectOne(`${API}/api/settings/upstream-disabled`).error(new ProgressEvent('network error'));

      // Failing closed here would blank the nav on any Poracle blip.
      expect(service.upstreamDisabled()).toEqual([]);
      expect(service.isDisabled('disable_raids')).toBe(false);
      expect(service.siteSettings()['site_name']).toBe('My Site');
    });

    it('never re-enables something the admin disabled here', () => {
      service.getAll().subscribe();
      flushGetAll([{ id: 1, category: 'features', key: 'disable_lures', value: 'true', valueType: 'boolean' }], []);

      expect(service.isDisabled('disable_lures')).toBe(true);
    });
  });

  describe('loadOnce', () => {
    it('should call getAll on first invocation', () => {
      service.loadOnce().subscribe();

      flushGetAll(mockSiteSettings);
    });

    it('should return empty and not make HTTP call when already loaded', () => {
      // Load first
      service.getAll().subscribe();
      flushGetAll(mockSiteSettings);

      // loadOnce should not make another request
      service.loadOnce().subscribe(result => {
        expect(result).toEqual([]);
      });

      httpMock.expectNone(`${API}/api/settings`);
      httpMock.expectNone(`${API}/api/settings/upstream-disabled`);
    });
  });

  describe('loadPublic', () => {
    it('should merge public SiteSetting response into existing settings', () => {
      // Pre-load some settings
      service.getAll().subscribe();
      flushGetAll(mockSiteSettings);

      // Load public settings
      service.loadPublic().subscribe();
      httpMock
        .expectOne(`${API}/api/settings/public`)
        .flush([{ id: 10, category: 'branding', key: 'site_name', value: 'Public Name', valueType: 'string' }]);

      expect(service.siteSettings()['site_name']).toBe('Public Name');
      // Existing settings should still be present
      expect(service.siteSettings()['enable_templates']).toBe('true');
    });

    it('should handle PwebSetting response shape in loadPublic', () => {
      service.loadPublic().subscribe();
      httpMock.expectOne(`${API}/api/settings/public`).flush([{ setting: 'site_name', value: 'Legacy' }]);

      expect(service.siteSettings()['site_name']).toBe('Legacy');
    });
  });

  describe('update', () => {
    it('should PUT updated setting with key/value body', () => {
      service.update('site_name', 'New Name').subscribe(result => {
        expect(result.value).toBe('New Name');
      });

      const req = httpMock.expectOne(`${API}/api/settings/site_name`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ key: 'site_name', value: 'New Name' });
      req.flush({ id: 3, category: 'branding', key: 'site_name', value: 'New Name', valueType: 'string' });
    });

    it('should encode special characters in the key', () => {
      service.update('key with spaces', 'val').subscribe();

      httpMock.expectOne(`${API}/api/settings/key%20with%20spaces`).flush({
        id: 1,
        category: 'misc',
        key: 'key with spaces',
        value: 'val',
        valueType: 'string',
      });
    });
  });
});
