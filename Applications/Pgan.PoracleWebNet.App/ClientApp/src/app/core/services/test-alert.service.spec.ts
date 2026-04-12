import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ConfigService } from './config.service';
import { TestAlertService } from './test-alert.service';

describe('TestAlertService', () => {
  let service: TestAlertService;
  let httpMock: HttpTestingController;
  let snackBar: jest.Mocked<MatSnackBar>;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
      ],
    });
    service = TestBed.inject(TestAlertService);
    httpMock = TestBed.inject(HttpTestingController);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should send test alert via HTTP POST', () => {
    service.sendTestAlert('pokemon', 42);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/42`);
    expect(req.request.method).toBe('POST');
    req.flush({ message: 'sent', status: 'ok' });
  });

  it('should show success snackbar on successful send', () => {
    service.sendTestAlert('raid', 7);

    const req = httpMock.expectOne(`${API}/api/test-alert/raid/7`);
    req.flush({ message: 'sent', status: 'ok' });

    expect(snackBar.open).toHaveBeenCalledWith('Test alert sent! Check your DMs.', 'OK', { duration: 4000 });
  });

  it('should show error snackbar on HTTP error', () => {
    service.sendTestAlert('quest', 5);

    const req = httpMock.expectOne(`${API}/api/test-alert/quest/5`);
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    expect(snackBar.open).toHaveBeenCalledWith('Failed to send test alert. Try again later.', 'OK', { duration: 4000 });
  });

  it('should show rate limit message on 429', () => {
    service.sendTestAlert('pokemon', 1);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/1`);
    req.flush('Too many requests', { status: 429, statusText: 'Too Many Requests' });

    expect(snackBar.open).toHaveBeenCalledWith('Too many test alerts. Please wait a moment.', 'OK', { duration: 4000 });
  });

  it('should show not found message on 404', () => {
    service.sendTestAlert('egg', 3);

    const req = httpMock.expectOne(`${API}/api/test-alert/egg/3`);
    req.flush('Not found', { status: 404, statusText: 'Not Found' });

    expect(snackBar.open).toHaveBeenCalledWith('Alarm not found — it may have been deleted.', 'OK', { duration: 4000 });
  });

  it('should surface the server message on 501 unsupported-type', () => {
    service.sendTestAlert('nest', 11);

    const req = httpMock.expectOne(`${API}/api/test-alert/nest/11`);
    req.flush(
      { error: "Nest test alerts aren't supported by PoracleNG's /api/test endpoint." },
      { status: 501, statusText: 'Not Implemented' },
    );

    expect(snackBar.open).toHaveBeenCalledWith("Nest test alerts aren't supported by PoracleNG's /api/test endpoint.", 'OK', {
      duration: 4000,
    });
  });

  it('should start a cooldown on 501 to avoid rate-limit burn from retry spam', () => {
    service.sendTestAlert('nest', 12);

    const req = httpMock.expectOne(`${API}/api/test-alert/nest/12`);
    req.flush({ error: 'Unsupported' }, { status: 501, statusText: 'Not Implemented' });

    expect(service.isCoolingDown('nest', 12)).toBe(true);
  });

  it('should fall back to a generic 501 message when the server omits the error body', () => {
    service.sendTestAlert('nest', 13);

    const req = httpMock.expectOne(`${API}/api/test-alert/nest/13`);
    req.flush(null, { status: 501, statusText: 'Not Implemented' });

    expect(snackBar.open).toHaveBeenCalledWith('Test alerts are not supported for this alarm type.', 'OK', { duration: 4000 });
  });

  it('should track sending state', () => {
    expect(service.isSending('pokemon', 10)).toBe(false);

    service.sendTestAlert('pokemon', 10);
    expect(service.isSending('pokemon', 10)).toBe(true);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/10`);
    req.flush({ message: 'sent', status: 'ok' });

    expect(service.isSending('pokemon', 10)).toBe(false);
  });

  it('should track cooldown after successful send', () => {
    service.sendTestAlert('raid', 20);

    const req = httpMock.expectOne(`${API}/api/test-alert/raid/20`);
    req.flush({ message: 'sent', status: 'ok' });

    expect(service.isCoolingDown('raid', 20)).toBe(true);
  });

  it('should prevent duplicate sends during cooldown', () => {
    service.sendTestAlert('pokemon', 99);
    httpMock.expectOne(`${API}/api/test-alert/pokemon/99`).flush({ message: 'sent', status: 'ok' });

    expect(service.isCoolingDown('pokemon', 99)).toBe(true);

    // Second call during cooldown should be a no-op (no HTTP request)
    service.sendTestAlert('pokemon', 99);
    httpMock.expectNone(`${API}/api/test-alert/pokemon/99`);
  });

  it('should prevent duplicate sends while in-flight', () => {
    service.sendTestAlert('raid', 50);
    expect(service.isSending('raid', 50)).toBe(true);

    // Second call while in-flight should be a no-op (no second HTTP request)
    service.sendTestAlert('raid', 50);

    // Only one request should have been made
    const requests = httpMock.match(`${API}/api/test-alert/raid/50`);
    expect(requests.length).toBe(1);
    requests[0].flush({ message: 'sent', status: 'ok' });
  });
});
