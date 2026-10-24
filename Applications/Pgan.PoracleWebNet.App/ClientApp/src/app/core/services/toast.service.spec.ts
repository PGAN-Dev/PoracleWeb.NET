import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, Observable, Subject, of } from 'rxjs';

import { PAIR_WARNING_DEFER_MS, PAIR_WARNING_TOAST_MS, ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;
  let snackBar: jest.Mocked<MatSnackBar>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: MatSnackBar, useValue: { open: jest.fn(() => ({ onAction: () => EMPTY })) } },
      ],
    });
    service = TestBed.inject(ToastService);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  describe('pairWarning', () => {
    const pairConfig = { duration: PAIR_WARNING_TOAST_MS, panelClass: ['toast-info'], verticalPosition: 'top' };
    let dismissed: Subject<{ dismissedByAction: boolean }>;

    /** A snack bar ref whose closing the test controls. */
    const ref = (action = EMPTY as Observable<void>) => {
      dismissed = new Subject();
      return { afterDismissed: () => dismissed, onAction: () => action };
    };

    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('stays up long enough to read, with an action that opens Linked accounts', () => {
      const go = jest.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref(of(undefined)));

      service.pairWarning('Saved for Discord, but not for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS);

      expect(snackBar.open).toHaveBeenCalledWith('Saved for Discord, but not for Telegram.', 'DRIFT.REVIEW', pairConfig);
      // It says why the page opened, so Linked accounts puts focus on its heading.
      expect(go).toHaveBeenCalledWith('/linked-accounts', { state: { driftReview: true } });
    });

    it("is carried by the page's own success toast instead of being replaced by it, and shown once", () => {
      service.pairWarning('Not saved for Telegram.');
      service.success('Profile renamed.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS + PAIR_WARNING_TOAST_MS);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      expect(snackBar.open).toHaveBeenCalledWith('Profile renamed. Not saved for Telegram.', 'DRIFT.REVIEW', pairConfig);
    });

    it('is carried by an info toast that comes after it is up, once', () => {
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      service.pairWarning('Not saved for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS);
      service.info('Areas saved.');
      dismissed.next({ dismissedByAction: false }); // replaced by the combined toast
      service.info('Something else.');

      expect(snackBar.open).toHaveBeenNthCalledWith(2, 'Areas saved. Not saved for Telegram.', 'DRIFT.REVIEW', pairConfig);
      expect(snackBar.open).toHaveBeenLastCalledWith('Something else.', 'TOAST.OK', expect.objectContaining({ duration: 3000 }));
      expect(snackBar.open).toHaveBeenCalledTimes(3);
    });

    it("waits for a page's own snack bar to close, then shows the warning after it", () => {
      const pageToast = new Subject<void>();
      Object.assign(snackBar, { _openedSnackBarRef: { afterDismissed: () => pageToast } });

      service.pairWarning('Not saved for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS);
      expect(snackBar.open).not.toHaveBeenCalled();

      Object.assign(snackBar, { _openedSnackBarRef: null });
      pageToast.next();
      expect(snackBar.open).toHaveBeenCalledWith('Not saved for Telegram.', 'DRIFT.REVIEW', pairConfig);
    });

    it('comes back once when another toast replaces it before it could be read', () => {
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      service.pairWarning('Not saved for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS + 500);

      const first = dismissed;
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      first.next({ dismissedByAction: false });
      expect(snackBar.open).toHaveBeenCalledTimes(2);

      // Replaced again: not a third time.
      dismissed.next({ dismissedByAction: false });
      expect(snackBar.open).toHaveBeenCalledTimes(2);
    });

    it('is not shown again after it ran its course, or after Review was pressed', () => {
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      service.pairWarning('Not saved for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS + PAIR_WARNING_TOAST_MS);
      dismissed.next({ dismissedByAction: false });
      expect(snackBar.open).toHaveBeenCalledTimes(1);

      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      service.pairWarning('Again.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS);
      dismissed.next({ dismissedByAction: true });
      expect(snackBar.open).toHaveBeenCalledTimes(2);
    });

    it('is not carried by a success long after it has gone', () => {
      (snackBar.open as jest.Mock).mockReturnValueOnce(ref());
      service.pairWarning('Not saved for Telegram.');
      jest.advanceTimersByTime(PAIR_WARNING_DEFER_MS + PAIR_WARNING_TOAST_MS + 1);
      service.success('Saved.');

      expect(snackBar.open).toHaveBeenLastCalledWith('Saved.', 'TOAST.OK', expect.objectContaining({ panelClass: ['toast-success'] }));
    });
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

  describe('info with a duration', () => {
    it('uses the one it is given', () => {
      service.info('A sentence with a reason', 8000);

      expect(snackBar.open).toHaveBeenCalledWith('A sentence with a reason', 'TOAST.OK', {
        duration: 8000,
        panelClass: ['toast-info'],
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
