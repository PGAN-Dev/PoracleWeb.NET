import { Injectable, Injector, inject } from '@angular/core';
import { MatSnackBar, MatSnackBarRef } from '@angular/material/snack-bar';
import { Router } from '@angular/router';

import { I18nService } from './i18n.service';

/** Longer than an error toast (5 s): a pair warning carries a reason sentence the person has to read. */
export const PAIR_WARNING_TOAST_MS = 8000;

/** How long a pair warning waits for the page's own toast about the same save, which would otherwise replace it. */
export const PAIR_WARNING_DEFER_MS = 50;

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  /**
   * The pair warning not yet read. The page that made the write shows its own toast a moment later, and a new toast
   * replaces the one on screen, so the warning was gone before anyone read it. A `success` or `info` through this
   * service carries it appended; a page that opens the snack bar itself goes first and the warning follows it.
   */
  private pairNote: null | { text: string; until: number } = null;
  private readonly snackBar = inject(MatSnackBar);

  error(message: string): void {
    this.snackBar.open(message, this.i18n.instant('TOAST.DISMISS'), {
      duration: 5000,
      panelClass: ['toast-error'],
      verticalPosition: 'top',
    });
  }

  /**
   * Map an HTTP error to a user-friendly message.
   */
  httpError(error: { status: number; error?: { error?: string; message?: string } }): void {
    let message: string;

    switch (error.status) {
      case 0:
        message = this.i18n.instant('HTTP_ERROR.NETWORK');
        break;
      case 400:
        message = error.error?.error || error.error?.message || this.i18n.instant('HTTP_ERROR.BAD_REQUEST');
        break;
      case 401:
        message = this.i18n.instant('HTTP_ERROR.UNAUTHORIZED');
        break;
      case 403:
        message = this.i18n.instant('HTTP_ERROR.FORBIDDEN');
        break;
      case 404:
        message = this.i18n.instant('HTTP_ERROR.NOT_FOUND');
        break;
      case 409:
        message = error.error?.error || this.i18n.instant('HTTP_ERROR.CONFLICT');
        break;
      case 429:
        message = this.i18n.instant('HTTP_ERROR.RATE_LIMIT');
        break;
      case 500:
        message = this.i18n.instant('HTTP_ERROR.SERVER_ERROR');
        break;
      case 502:
      case 503:
      case 504:
        message = this.i18n.instant('HTTP_ERROR.UNAVAILABLE');
        break;
      default:
        message = error.error?.error || error.error?.message || this.i18n.instant('HTTP_ERROR.GENERIC');
    }

    this.error(message);
  }

  info(message: string, duration = 3000): void {
    const note = this.takePairNote();
    if (note) {
      this.openPairWarning(`${message} ${note}`);
      return;
    }
    this.snackBar.open(message, this.i18n.instant('TOAST.OK'), {
      duration,
      panelClass: ['toast-info'],
      verticalPosition: 'top',
    });
  }

  /**
   * Says that a write reached only one of a linked pair's apps. Its action opens Linked accounts, where the
   * differences are listed. A success or info toast shown while it is up carries it rather than replacing it.
   */
  pairWarning(message: string): void {
    const note = { text: message, until: Date.now() + PAIR_WARNING_DEFER_MS + PAIR_WARNING_TOAST_MS };
    this.pairNote = note;
    setTimeout(() => this.showPairWarning(note, 1), PAIR_WARNING_DEFER_MS);
  }

  success(message: string): void {
    const note = this.takePairNote();
    if (note) {
      this.openPairWarning(`${message} ${note}`);
      return;
    }
    this.snackBar.open(message, this.i18n.instant('TOAST.OK'), {
      duration: 3000,
      panelClass: ['toast-success'],
      verticalPosition: 'top',
    });
  }

  private openPairWarning(text: string): MatSnackBarRef<unknown> | undefined {
    const ref = this.snackBar.open(text, this.i18n.instant('DRIFT.REVIEW'), {
      duration: PAIR_WARNING_TOAST_MS,
      panelClass: ['toast-info'],
      verticalPosition: 'top',
    });
    ref?.onAction().subscribe(() => void this.injector.get(Router).navigateByUrl('/linked-accounts'));
    return ref;
  }

  /**
   * Shows the warning once whatever toast is up has gone, and once more if another toast replaces it before it could
   * be read. Skipped when a page's toast already carried it.
   */
  private showPairWarning(note: { text: string; until: number }, retries: number): void {
    if (this.pairNote !== note) return;
    const open = this.snackBar._openedSnackBarRef;
    if (open) {
      open.afterDismissed().subscribe(() => this.showPairWarning(note, retries));
      return;
    }
    const shownAt = Date.now();
    note.until = shownAt + PAIR_WARNING_TOAST_MS;
    this.openPairWarning(note.text)
      ?.afterDismissed()
      .subscribe(({ dismissedByAction }) => {
        if (dismissedByAction || retries <= 0 || this.pairNote !== note) return;
        if (Date.now() - shownAt >= PAIR_WARNING_TOAST_MS - 250) {
          this.pairNote = null;
          return;
        }
        this.showPairWarning(note, retries - 1);
      });
  }

  private takePairNote(): null | string {
    const note = this.pairNote;
    this.pairNote = null;
    return note && Date.now() < note.until ? note.text : null;
  }
}
