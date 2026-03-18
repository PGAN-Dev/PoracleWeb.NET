import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { PwebSetting } from '../models';

describe('SettingsService', () => {
  let service: SettingsService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockSettings: PwebSetting[] = [
    { setting: 'enable_templates', value: 'true' },
    { setting: 'disable_raids', value: 'false' },
    { setting: 'site_name', value: 'My Site' },
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

  describe('getAll', () => {
    it('should fetch settings and populate siteSettings signal', () => {
      service.getAll().subscribe(settings => {
        expect(settings).toHaveLength(3);
      });

      httpMock.expectOne(`${API}/api/settings`).flush(mockSettings);

      expect(service.siteSettings()['enable_templates']).toBe('true');
      expect(service.siteSettings()['site_name']).toBe('My Site');
    });

    it('should only populate siteSettings once on first call', () => {
      // First call
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush(mockSettings);

      expect(service.siteSettings()['enable_templates']).toBe('true');

      // Second call - should still make HTTP request but not re-populate
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush([{ setting: 'enable_templates', value: 'false' }]);

      // Should still have old value since loaded flag is true
      expect(service.siteSettings()['enable_templates']).toBe('true');
    });

    it('should handle settings with null values', () => {
      service.getAll().subscribe();

      httpMock.expectOne(`${API}/api/settings`).flush([{ setting: 'key1', value: null }]);

      expect(service.siteSettings()['key1']).toBe('');
    });
  });

  describe('getConfig', () => {
    it('should fetch poracle config', () => {
      service.getConfig().subscribe(config => {
        expect(config.areas).toHaveLength(0);
      });

      httpMock.expectOne(`${API}/api/settings/config`).flush({
        areas: [],
        forms: {},
        grunts: {},
        items: {},
        moves: {},
        pokemon: {},
      });
    });
  });

  describe('isDisabled', () => {
    it('should return true when setting is "true"', () => {
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush([{ setting: 'disable_raids', value: 'true' }]);

      expect(service.isDisabled('disable_raids')).toBe(true);
    });

    it('should return true case-insensitively', () => {
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush([{ setting: 'disable_raids', value: 'True' }]);

      expect(service.isDisabled('disable_raids')).toBe(true);
    });

    it('should return false when setting is not "true"', () => {
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush([{ setting: 'disable_raids', value: 'false' }]);

      expect(service.isDisabled('disable_raids')).toBe(false);
    });

    it('should return false for non-existent settings', () => {
      expect(service.isDisabled('nonexistent')).toBe(false);
    });
  });

  describe('loadOnce', () => {
    it('should call getAll on first invocation', () => {
      service.loadOnce().subscribe();

      httpMock.expectOne(`${API}/api/settings`).flush(mockSettings);
    });

    it('should return empty and not make HTTP call when already loaded', () => {
      // Load first
      service.getAll().subscribe();
      httpMock.expectOne(`${API}/api/settings`).flush(mockSettings);

      // loadOnce should not make another request
      service.loadOnce().subscribe(result => {
        expect(result).toEqual([]);
      });

      httpMock.expectNone(`${API}/api/settings`);
    });
  });

  describe('update', () => {
    it('should PUT updated setting', () => {
      service.update('site_name', 'New Name').subscribe(result => {
        expect(result.value).toBe('New Name');
      });

      const req = httpMock.expectOne(`${API}/api/settings/site_name`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ setting: 'site_name', value: 'New Name' });
      req.flush({ setting: 'site_name', value: 'New Name' });
    });

    it('should encode special characters in the key', () => {
      service.update('key with spaces', 'val').subscribe();

      httpMock.expectOne(`${API}/api/settings/key%20with%20spaces`).flush({
        setting: 'key with spaces',
        value: 'val',
      });
    });
  });
});
