import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

import { I18nService } from './i18n.service';

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly i18n = inject(I18nService);
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

  info(message: string): void {
    this.snackBar.open(message, this.i18n.instant('TOAST.OK'), {
      duration: 3000,
      panelClass: ['toast-info'],
      verticalPosition: 'top',
    });
  }

  success(message: string): void {
    this.snackBar.open(message, this.i18n.instant('TOAST.OK'), {
      duration: 3000,
      panelClass: ['toast-success'],
      verticalPosition: 'top',
    });
  }
}
