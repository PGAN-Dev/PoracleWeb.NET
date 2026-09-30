import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';

import { ConfigService } from './config.service';
import { TestAlertService } from './test-alert.service';
import { ToastService } from './toast.service';
import { errorInterceptor } from '../interceptors/error.interceptor';

/**
 * A failed test alert raised two snackbars: the interceptor's generic "An unexpected server error
 * occurred" and TestAlertService's own message, racing for the one slot. The user saw the generic one.
 * TestAlertService owns every outcome of its request, so the interceptor stays out of it.
 */
describe('TestAlertService with the error interceptor', () => {
  const API = 'http://test-api';
  let httpMock: HttpTestingController;
  let service: TestAlertService;
  let snackBar: { open: jest.Mock };
  let toast: { error: jest.Mock };

  beforeEach(() => {
    snackBar = { open: jest.fn() };
    toast = { error: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: ToastService, useValue: toast },
        { provide: Router, useValue: { navigate: jest.fn() } },
        { provide: TranslateService, useValue: { instant: jest.fn((key: string) => key) } },
      ],
    });
    service = TestBed.inject(TestAlertService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const fail = (body: unknown, status: number) => {
    service.sendTestAlert('pokemon', 7);
    httpMock.expectOne(`${API}/api/test-alert/pokemon/7`).flush(body, { status, statusText: 'x' });
  };

  const messages = () => snackBar.open.mock.calls.map(call => call[0]);

  it('shows only the test-alert message when sending fails', () => {
    fail({ error: 'Failed to send test alert' }, 500);

    expect(toast.error).not.toHaveBeenCalled();
    expect(messages()).toEqual(['TEST_ALERT.FAILED']);
  });

  it('shows only the rate-limit message on 429', () => {
    fail('Too many requests', 429);

    expect(toast.error).not.toHaveBeenCalled();
    expect(messages()).toEqual(['TEST_ALERT.RATE_LIMITED']);
  });

  it("shows only the server's reason on 501", () => {
    fail({ error: 'Nest alarms cannot be test-fired.' }, 501);

    expect(toast.error).not.toHaveBeenCalled();
    expect(messages()).toEqual(['Nest alarms cannot be test-fired.']);
  });

  it('says the feature is disabled on a feature-disabled 403, once', () => {
    fail({ disableKey: 'disable_mons', error: 'disabled' }, 403);

    expect(toast.error).not.toHaveBeenCalled();
    expect(messages()).toEqual(['ERROR.FEATURE_DISABLED']);
  });

  it('still reports success', () => {
    service.sendTestAlert('pokemon', 7);
    httpMock.expectOne(`${API}/api/test-alert/pokemon/7`).flush({ status: 'ok' });

    expect(messages()).toEqual(['TEST_ALERT.SUCCESS']);
  });

  /** Only the test-alert route is silenced; every other failure still reaches the user. */
  it('leaves the interceptor toasting other endpoints', () => {
    TestBed.inject(HttpClient)
      .get(`${API}/api/dashboard`)
      .subscribe({ error: () => undefined });
    httpMock.expectOne(`${API}/api/dashboard`).flush(null, { status: 500, statusText: 'x' });

    expect(toast.error).toHaveBeenCalledWith('HTTP_ERROR.SERVER_ERROR');
  });
});
