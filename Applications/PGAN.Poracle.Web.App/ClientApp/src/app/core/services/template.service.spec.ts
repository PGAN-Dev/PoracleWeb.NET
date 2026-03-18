import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { TemplateService, TemplateData } from './template.service';

describe('TemplateService', () => {
  let service: TemplateService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockTemplateData: TemplateData = {
    discord: {
      monster: { default: ['template1', 'template2'] },
      raid: { default: ['raid_template'] },
    },
    status: 'ok',
    telegram: {
      monster: { default: ['tg_template1'] },
    },
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        {
          provide: AuthService,
          useValue: { user: () => ({ type: 'discord:user' }) },
        },
      ],
    });
    service = TestBed.inject(TemplateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('loadTemplates', () => {
    it('should fetch templates from API', () => {
      service.loadTemplates().subscribe(data => {
        expect(data.status).toBe('ok');
      });

      httpMock.expectOne(`${API}/api/config/templates`).flush(mockTemplateData);
    });

    it('should only fetch once and cache via ReplaySubject', () => {
      service.loadTemplates().subscribe();
      service.loadTemplates().subscribe();

      // Only one HTTP request
      httpMock.expectOne(`${API}/api/config/templates`).flush(mockTemplateData);
    });

    it('should return empty data on error and allow retry', () => {
      service.loadTemplates().subscribe(data => {
        expect(data.discord).toEqual({});
      });

      httpMock.expectOne(`${API}/api/config/templates`).flush(null, {
        status: 500, statusText: 'Error',
      });
    });
  });

  describe('getTemplatesForType', () => {
    it('should return discord templates for discord users', () => {
      service.getTemplatesForType('monster').subscribe(templates => {
        expect(templates).toContain('template1');
        expect(templates).toContain('template2');
      });

      httpMock.expectOne(`${API}/api/config/templates`).flush(mockTemplateData);
    });

    it('should return telegram templates for telegram users', () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ConfigService, useValue: { apiHost: API } },
          {
            provide: AuthService,
            useValue: { user: () => ({ type: 'telegram:user' }) },
          },
        ],
      });
      const tgService = TestBed.inject(TemplateService);
      const tgHttpMock = TestBed.inject(HttpTestingController);

      tgService.getTemplatesForType('monster').subscribe(templates => {
        expect(templates).toContain('tg_template1');
      });

      tgHttpMock.expectOne(`${API}/api/config/templates`).flush(mockTemplateData);
    });

    it('should return empty array for non-existent type', () => {
      service.getTemplatesForType('nonexistent').subscribe(templates => {
        expect(templates).toEqual([]);
      });

      httpMock.expectOne(`${API}/api/config/templates`).flush(mockTemplateData);
    });

    it('should deduplicate templates across categories', () => {
      const dataWithDupes: TemplateData = {
        discord: {
          monster: {
            cat1: ['template1', 'template2'],
            cat2: ['template1', 'template3'],
          },
        },
        status: 'ok',
        telegram: {},
      };

      service.getTemplatesForType('monster').subscribe(templates => {
        expect(templates).toHaveLength(3);
        expect(templates).toContain('template1');
        expect(templates).toContain('template2');
        expect(templates).toContain('template3');
      });

      httpMock.expectOne(`${API}/api/config/templates`).flush(dataWithDupes);
    });
  });
});
