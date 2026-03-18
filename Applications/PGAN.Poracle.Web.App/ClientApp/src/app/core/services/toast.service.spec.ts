import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;
  let snackBar: jest.Mocked<MatSnackBar>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: MatSnackBar, useValue: { open: jest.fn() } }],
    });
    service = TestBed.inject(ToastService);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  describe('success', () => {
    it('should show success toast with correct config', () => {
      service.success('Item saved!');

      expect(snackBar.open).toHaveBeenCalledWith('Item saved!', 'OK', {
        duration: 3000,
        panelClass: ['toast-success'],
      });
    });
  });

  describe('error', () => {
    it('should show error toast with longer duration', () => {
      service.error('Something failed');

      expect(snackBar.open).toHaveBeenCalledWith('Something failed', 'Dismiss', {
        duration: 5000,
        panelClass: ['toast-error'],
      });
    });
  });

  describe('info', () => {
    it('should show info toast', () => {
      service.info('FYI');

      expect(snackBar.open).toHaveBeenCalledWith('FYI', 'OK', {
        duration: 3000,
        panelClass: ['toast-info'],
      });
    });
  });

  describe('httpError', () => {
    it('should show connection error for status 0', () => {
      service.httpError({ status: 0 });

      expect(snackBar.open).toHaveBeenCalledWith(
        'Unable to reach the server. Please check your connection.',
        'Dismiss',
        expect.objectContaining({ duration: 5000 }),
      );
    });

    it('should show custom error message from 400 response body', () => {
      service.httpError({
        error: { error: 'Invalid Pokemon ID' },
        status: 400,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Invalid Pokemon ID', 'Dismiss', expect.anything());
    });

    it('should show fallback for 400 without body error', () => {
      service.httpError({ status: 400 });

      expect(snackBar.open).toHaveBeenCalledWith('Invalid request. Please check your input.', 'Dismiss', expect.anything());
    });

    it('should show session expired for 401', () => {
      service.httpError({ status: 401 });

      expect(snackBar.open).toHaveBeenCalledWith('Your session has expired. Please sign in again.', 'Dismiss', expect.anything());
    });

    it('should show permission denied for 403', () => {
      service.httpError({ status: 403 });

      expect(snackBar.open).toHaveBeenCalledWith('You do not have permission to perform this action.', 'Dismiss', expect.anything());
    });

    it('should show not found for 404', () => {
      service.httpError({ status: 404 });

      expect(snackBar.open).toHaveBeenCalledWith('The requested resource was not found.', 'Dismiss', expect.anything());
    });

    it('should show conflict for 409 with custom error', () => {
      service.httpError({
        error: { error: 'Profile already exists' },
        status: 409,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Profile already exists', 'Dismiss', expect.anything());
    });

    it('should show default conflict for 409 without custom error', () => {
      service.httpError({ status: 409 });

      expect(snackBar.open).toHaveBeenCalledWith('A conflict occurred. The item may have been modified.', 'Dismiss', expect.anything());
    });

    it('should show rate limit for 429', () => {
      service.httpError({ status: 429 });

      expect(snackBar.open).toHaveBeenCalledWith('Too many requests. Please wait a moment and try again.', 'Dismiss', expect.anything());
    });

    it('should show server error for 500', () => {
      service.httpError({ status: 500 });

      expect(snackBar.open).toHaveBeenCalledWith(
        'An unexpected server error occurred. Please try again later.',
        'Dismiss',
        expect.anything(),
      );
    });

    it('should show unavailable for 502/503/504', () => {
      for (const status of [502, 503, 504]) {
        jest.clearAllMocks();
        service.httpError({ status });

        expect(snackBar.open).toHaveBeenCalledWith(
          'The server is temporarily unavailable. Please try again shortly.',
          'Dismiss',
          expect.anything(),
        );
      }
    });

    it('should show generic message for unknown status codes', () => {
      service.httpError({ status: 418 });

      expect(snackBar.open).toHaveBeenCalledWith('Something went wrong. Please try again.', 'Dismiss', expect.anything());
    });

    it('should prefer error.error over error.message in default case', () => {
      service.httpError({
        error: { error: 'Teapot error', message: 'fallback' },
        status: 418,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Teapot error', 'Dismiss', expect.anything());
    });

    it('should fall back to error.message when error.error is absent in 400', () => {
      service.httpError({
        error: { message: 'Missing field: name' },
        status: 400,
      });

      expect(snackBar.open).toHaveBeenCalledWith('Missing field: name', 'Dismiss', expect.anything());
    });
  });
});
