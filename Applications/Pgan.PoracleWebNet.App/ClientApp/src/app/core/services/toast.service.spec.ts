import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';

import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;
  let snackBar: jest.Mocked<MatSnackBar>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MatSnackBar, useValue: { open: jest.fn() } }],
    });
    service = TestBed.inject(ToastService);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  describe('success', () => {
    it('should show success toast with correct config', () => {
      service.success('Item saved!');

      expect(snackBar.open).toHaveBeenCalledWith('Item saved!', 'TOAST.OK', {
        duration: 3000,
        panelClass: ['toast-success'],
        verticalPosition: 'top',
      });
    });
  });

  describe('error', () => {
    it('should show error toast with longer duration', () => {
      service.error('Something failed');

      expect(snackBar.open).toHaveBeenCalledWith('Something failed', 'TOAST.DISMISS', {
        duration: 5000,
        panelClass: ['toast-error'],
        verticalPosition: 'top',
      });
    });
  });

  describe('info', () => {
    it('should show info toast', () => {
      service.info('FYI');

      expect(snackBar.open).toHaveBeenCalledWith('FYI', 'TOAST.OK', {
        duration: 3000,
        panelClass: ['toast-info'],
        verticalPosition: 'top',
      });
    });
  });

  describe('httpError', () => {
    it('should show connection error for status 0', () => {
      service.httpError({ status: 0 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.NETWORK', 'TOAST.DISMISS', expect.objectContaining({ duration: 5000 }));
    });

    it('should show custom error message from 400 response body', () => {
      service.httpError({
        error: { error: 'Invalid Pokemon ID' },
        status: 400,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Invalid Pokemon ID', 'TOAST.DISMISS', expect.anything());
    });

    it('should show fallback for 400 without body error', () => {
      service.httpError({ status: 400 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.BAD_REQUEST', 'TOAST.DISMISS', expect.anything());
    });

    it('should show session expired for 401', () => {
      service.httpError({ status: 401 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.UNAUTHORIZED', 'TOAST.DISMISS', expect.anything());
    });

    it('should show permission denied for 403', () => {
      service.httpError({ status: 403 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.FORBIDDEN', 'TOAST.DISMISS', expect.anything());
    });

    it('should show not found for 404', () => {
      service.httpError({ status: 404 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.NOT_FOUND', 'TOAST.DISMISS', expect.anything());
    });

    it('should show conflict for 409 with custom error', () => {
      service.httpError({
        error: { error: 'Profile already exists' },
        status: 409,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Profile already exists', 'TOAST.DISMISS', expect.anything());
    });

    it('should show default conflict for 409 without custom error', () => {
      service.httpError({ status: 409 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.CONFLICT', 'TOAST.DISMISS', expect.anything());
    });

    it('should show rate limit for 429', () => {
      service.httpError({ status: 429 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.RATE_LIMIT', 'TOAST.DISMISS', expect.anything());
    });

    it('should show server error for 500', () => {
      service.httpError({ status: 500 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.SERVER_ERROR', 'TOAST.DISMISS', expect.anything());
    });

    it('should show unavailable for 502/503/504', () => {
      for (const status of [502, 503, 504]) {
        jest.clearAllMocks();
        service.httpError({ status });

        expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.UNAVAILABLE', 'TOAST.DISMISS', expect.anything());
      }
    });

    it('should show generic message for unknown status codes', () => {
      service.httpError({ status: 418 });

      expect(snackBar.open).toHaveBeenCalledWith('HTTP_ERROR.GENERIC', 'TOAST.DISMISS', expect.anything());
    });

    it('should prefer error.error over error.message in default case', () => {
      service.httpError({
        error: { error: 'Teapot error', message: 'fallback' },
        status: 418,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Teapot error', 'TOAST.DISMISS', expect.anything());
    });

    it('should fall back to error.message when error.error is missing', () => {
      service.httpError({
        error: { message: 'Some message' },
        status: 418,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Some message', 'TOAST.DISMISS', expect.anything());
    });
  });
});
