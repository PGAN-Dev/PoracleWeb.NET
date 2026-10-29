import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { TestAlertService } from './test-alert.service';
import { UserInfo } from '../models';

describe('TestAlertService', () => {
  let service: TestAlertService;
  let httpMock: HttpTestingController;
  let snackBar: jest.Mocked<MatSnackBar>;
  let translate: { instant: jest.Mock };
  let auth: { isImpersonating: WritableSignal<boolean>; user: WritableSignal<Partial<UserInfo> | null> };
  const API = 'http://test-api';

  beforeEach(() => {
    translate = { instant: jest.fn((key: string) => key) };
    auth = { isImpersonating: signal(false), user: signal(null) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: TranslateService, useValue: translate },
        { provide: AuthService, useValue: auth },
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

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.SUCCESS', 'OK', { duration: 4000 });
  });

  it("says where a destination's test alert went", () => {
    // Managing a channel, "Check your DMs" sends the admin to look in the wrong place.
    auth.isImpersonating.set(true);
    auth.user.set({ username: '#raid-alerts', type: 'discord:channel' });

    service.sendTestAlert('raid', 7);
    httpMock.expectOne(`${API}/api/test-alert/raid/7`).flush({ message: 'sent', status: 'ok' });

    expect(translate.instant).toHaveBeenCalledWith('TEST_ALERT.SUCCESS_DESTINATION', { name: '#raid-alerts' });
    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.SUCCESS_DESTINATION', 'OK', { duration: 4000 });
  });

  it('still says DMs while inspecting a person', () => {
    auth.isImpersonating.set(true);
    auth.user.set({ username: 'Ash', type: 'discord:user' });

    service.sendTestAlert('raid', 8);
    httpMock.expectOne(`${API}/api/test-alert/raid/8`).flush({ message: 'sent', status: 'ok' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.SUCCESS', 'OK', { duration: 4000 });
  });

  it('should show error snackbar on HTTP error', () => {
    service.sendTestAlert('quest', 5);

    const req = httpMock.expectOne(`${API}/api/test-alert/quest/5`);
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.FAILED', 'OK', { duration: 4000 });
  });

  it('should show rate limit message on 429', () => {
    service.sendTestAlert('pokemon', 1);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/1`);
    req.flush('Too many requests', { status: 429, statusText: 'Too Many Requests' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.RATE_LIMITED', 'OK', { duration: 4000 });
  });

  it('should show not found message on 404', () => {
    service.sendTestAlert('egg', 3);

    const req = httpMock.expectOne(`${API}/api/test-alert/egg/3`);
    req.flush('Not found', { status: 404, statusText: 'Not Found' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.NOT_FOUND', 'OK', { duration: 4000 });
  });

  it('should surface the server message on 501 unsupported-type', () => {
    service.sendTestAlert('nest', 11);

    const req = httpMock.expectOne(`${API}/api/test-alert/nest/11`);
    req.flush(
      { error: "Nest test notifications aren't supported by PoracleNG's /api/test endpoint." },
      { status: 501, statusText: 'Not Implemented' },
    );

    expect(snackBar.open).toHaveBeenCalledWith("Nest test notifications aren't supported by PoracleNG's /api/test endpoint.", 'OK', {
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

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.UNSUPPORTED', 'OK', { duration: 4000 });
  });

  // PoracleNG said why delivery failed (alarms D16): "try again later" is wrong when the cause is configuration.
  it("should pass on PoracleNG's reason when it refused the test (502)", () => {
    service.sendTestAlert('pokemon', 14);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/14`);
    req.flush({ error: 'message delivery not configured' }, { status: 502, statusText: 'Bad Gateway' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.REFUSED', 'OK', { duration: 8000 });
    expect(TestBed.inject(TranslateService).instant).toHaveBeenCalledWith('TEST_ALERT.REFUSED', {
      reason: 'message delivery not configured',
    });
  });

  it('should keep the generic message for a 502 that carries no reason', () => {
    service.sendTestAlert('pokemon', 15);

    const req = httpMock.expectOne(`${API}/api/test-alert/pokemon/15`);
    req.flush(null, { status: 502, statusText: 'Bad Gateway' });

    expect(snackBar.open).toHaveBeenCalledWith('TEST_ALERT.FAILED', 'OK', { duration: 4000 });
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
