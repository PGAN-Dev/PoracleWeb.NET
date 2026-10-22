import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { RuleRepairService } from './rule-repair.service';
import { RuleRepairStatus } from '../models';

const status = (phase: RuleRepairStatus['phase']): RuleRepairStatus => ({
  applied: null,
  cannotEdit: [],
  cannotEditCount: 0,
  error: null,
  finishedAt: null,
  humansDone: 0,
  humansTotal: 0,
  phase,
  repairable: [],
  repairableCount: 0,
  skipped: [],
  skippedCount: 0,
  startedAt: null,
});

describe('RuleRepairService', () => {
  let service: RuleRepairService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: 'http://api' } }],
    });
    service = TestBed.inject(RuleRepairService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the status', () => {
    let got: RuleRepairStatus | undefined;
    service.getStatus().subscribe(s => (got = s));
    const req = http.expectOne('http://api/api/admin/rule-repair');
    expect(req.request.method).toBe('GET');
    req.flush(status('Scanned'));
    expect(got?.phase).toBe('Scanned');
  });

  it('reads a summary, without the lists, when asked', () => {
    let got: RuleRepairStatus | undefined;
    service.getStatus(true).subscribe(s => (got = s));
    const req = http.expectOne('http://api/api/admin/rule-repair?summary=true');
    expect(req.request.method).toBe('GET');
    req.flush({ ...status('Scanning'), repairableCount: 7 });
    expect(got?.repairableCount).toBe(7);
  });

  it('starts a scan with a POST', () => {
    service.scan().subscribe();
    const req = http.expectOne('http://api/api/admin/rule-repair/scan');
    expect(req.request.method).toBe('POST');
    req.flush(status('Scanning'), { status: 202, statusText: 'Accepted' });
  });

  it('starts an apply with a POST', () => {
    service.apply().subscribe();
    const req = http.expectOne('http://api/api/admin/rule-repair/apply');
    expect(req.request.method).toBe('POST');
    req.flush(status('Applying'), { status: 202, statusText: 'Accepted' });
  });

  it('turns a 409 into the current status instead of an error', () => {
    let got: RuleRepairStatus | undefined;
    let failed = false;
    service.scan().subscribe({ error: () => (failed = true), next: s => (got = s) });
    http.expectOne('http://api/api/admin/rule-repair/scan').flush(status('Scanning'), { status: 409, statusText: 'Conflict' });
    expect(failed).toBe(false);
    expect(got?.phase).toBe('Scanning');
  });

  it('still fails on other errors', () => {
    let failed = false;
    service.apply().subscribe({ error: () => (failed = true) });
    http.expectOne('http://api/api/admin/rule-repair/apply').flush({}, { status: 500, statusText: 'Server Error' });
    expect(failed).toBe(true);
  });
});
