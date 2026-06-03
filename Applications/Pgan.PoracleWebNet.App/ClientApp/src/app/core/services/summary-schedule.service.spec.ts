import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { SummaryScheduleService } from './summary-schedule.service';
import { ActiveHourEntry } from '../models/active-hours.models';

describe('SummaryScheduleService', () => {
  let service: SummaryScheduleService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';
  const BASE = `${API}/api/summary-schedules`;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(SummaryScheduleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('enabled signal defaults to false', () => {
    expect(service.enabled()).toBe(false);
  });

  describe('getSchedules', () => {
    it('GETs /api/summary-schedules and parses each activeHours string into entries', () => {
      let result: { alertType: string; activeHours: ActiveHourEntry[] }[] | undefined;
      service.getSchedules().subscribe(res => (result = res));

      const req = httpMock.expectOne(BASE);
      expect(req.request.method).toBe('GET');
      req.flush([{ activeHours: '[{"day":1,"hours":9,"mins":0}]', alertType: 'quest' }]);

      expect(result).toHaveLength(1);
      expect(result![0].alertType).toBe('quest');
      expect(result![0].activeHours).toEqual([{ day: 1, hours: 9, mins: 0 }]);
    });

    it('returns an empty list when the API returns no schedules', () => {
      let result: unknown[] | undefined;
      service.getSchedules().subscribe(res => (result = res));

      httpMock.expectOne(BASE).flush([]);

      expect(result).toEqual([]);
    });

    it('coerces PoracleNG string-typed hours/mins to numbers', () => {
      let result: { activeHours: ActiveHourEntry[] }[] | undefined;
      service.getSchedules().subscribe(res => (result = res));

      httpMock.expectOne(BASE).flush([{ activeHours: '[{"day":"2","hours":"08","mins":"30"}]', alertType: 'quest' }]);

      expect(result![0].activeHours).toEqual([{ day: 2, hours: 8, mins: 30 }]);
    });
  });

  describe('getSchedule', () => {
    it('GETs /{alertType} and maps the response to a SummarySchedule', () => {
      let result: { alertType: string; activeHours: ActiveHourEntry[] } | null | undefined;
      service.getSchedule('quest').subscribe(res => (result = res));

      const req = httpMock.expectOne(`${BASE}/quest`);
      expect(req.request.method).toBe('GET');
      req.flush({ activeHours: '[{"day":5,"hours":18,"mins":0}]', alertType: 'quest' });

      expect(result).toEqual({ activeHours: [{ day: 5, hours: 18, mins: 0 }], alertType: 'quest' });
    });

    it('maps a 404 to null (a missing schedule is normal, not an error)', () => {
      let result: unknown = 'unset';
      service.getSchedule('quest').subscribe(res => (result = res));

      httpMock.expectOne(`${BASE}/quest`).flush({ error: 'schedule not found' }, { status: 404, statusText: 'Not Found' });

      expect(result).toBeNull();
    });

    it('maps a 503 outage to null without throwing', () => {
      let result: unknown = 'unset';
      let errored = false;
      service.getSchedule('quest').subscribe({
        error: () => (errored = true),
        next: res => (result = res),
      });

      httpMock.expectOne(`${BASE}/quest`).flush('unavailable', { status: 503, statusText: 'Service Unavailable' });

      expect(errored).toBe(false);
      expect(result).toBeNull();
    });
  });

  describe('setSchedule', () => {
    it('PUTs /{alertType} with the stringified entries array', () => {
      const hours: ActiveHourEntry[] = [
        { day: 1, hours: 9, mins: 0 },
        { day: 2, hours: 9, mins: 0 },
      ];
      service.setSchedule('quest', hours).subscribe();

      const req = httpMock.expectOne(`${BASE}/quest`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ activeHours: JSON.stringify(hours) });
      req.flush(null);
    });

    it('PUTs "[]" when passed null (clear without deleting the row)', () => {
      service.setSchedule('quest', null).subscribe();

      const req = httpMock.expectOne(`${BASE}/quest`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ activeHours: '[]' });
      req.flush(null);
    });

    it('PUTs "[]" when passed an empty array', () => {
      service.setSchedule('quest', []).subscribe();

      const req = httpMock.expectOne(`${BASE}/quest`);
      expect(req.request.body).toEqual({ activeHours: '[]' });
      req.flush(null);
    });
  });

  describe('deleteSchedule', () => {
    it('DELETEs /{alertType}', () => {
      service.deleteSchedule('quest').subscribe();

      const req = httpMock.expectOne(`${BASE}/quest`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  describe('trigger', () => {
    it('POSTs /{alertType}/trigger', () => {
      service.trigger('quest').subscribe();

      const req = httpMock.expectOne(`${BASE}/quest/trigger`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });
  });

  describe('loadCapability', () => {
    it('GETs /capability and sets enabled=true from a 200 body', () => {
      service.loadCapability();

      const req = httpMock.expectOne(`${BASE}/capability`);
      expect(req.request.method).toBe('GET');
      req.flush({ enabled: true });

      expect(service.enabled()).toBe(true);
    });

    it('sets enabled=false when the API reports the feature off', () => {
      service.loadCapability();

      httpMock.expectOne(`${BASE}/capability`).flush({ enabled: false });

      expect(service.enabled()).toBe(false);
    });

    it('defaults to false and does not throw on a 503 outage', () => {
      service.loadCapability();

      httpMock.expectOne(`${BASE}/capability`).flush('unavailable', { status: 503, statusText: 'Service Unavailable' });

      expect(service.enabled()).toBe(false);
    });

    it('preserves the prior enabled value on a transient capability error', () => {
      // First load succeeds and flips the signal on.
      service.loadCapability();
      httpMock.expectOne(`${BASE}/capability`).flush({ enabled: true });
      expect(service.enabled()).toBe(true);

      // A later refresh that errors must not flip the panel off.
      (service as unknown as { fetchCapability: () => void }).fetchCapability();
      httpMock.expectOne(`${BASE}/capability`).flush('boom', { status: 500, statusText: 'Internal Server Error' });

      expect(service.enabled()).toBe(true);
    });

    it('is idempotent — two calls issue exactly one capability request', () => {
      service.loadCapability();
      service.loadCapability();

      const requests = httpMock.match(`${BASE}/capability`);
      expect(requests.length).toBe(1);
      requests[0].flush({ enabled: true });
    });
  });
});
