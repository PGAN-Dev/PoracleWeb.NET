import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly snackBar = inject(MatSnackBar);

  error(message: string): void {
    this.snackBar.open(message, 'Dismiss', {
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
        message = 'Unable to reach the server. Please check your connection.';
        break;
      case 400:
        message = error.error?.error || error.error?.message || 'Invalid request. Please check your input.';
        break;
      case 401:
        message = 'Your session has expired. Please sign in again.';
        break;
      case 403:
        message = 'You do not have permission to perform this action.';
        break;
      case 404:
        message = 'The requested resource was not found.';
        break;
      case 409:
        message = error.error?.error || 'A conflict occurred. The item may have been modified.';
        break;
      case 429:
        message = 'Too many requests. Please wait a moment and try again.';
        break;
      case 500:
        message = 'An unexpected server error occurred. Please try again later.';
        break;
      case 502:
      case 503:
      case 504:
        message = 'The server is temporarily unavailable. Please try again shortly.';
        break;
      default:
        message = error.error?.error || error.error?.message || 'Something went wrong. Please try again.';
    }

    this.error(message);
  }

  info(message: string): void {
    this.snackBar.open(message, 'OK', {
      duration: 3000,
      panelClass: ['toast-info'],
      verticalPosition: 'top',
    });
  }

  success(message: string): void {
    this.snackBar.open(message, 'OK', {
      duration: 3000,
      panelClass: ['toast-success'],
      verticalPosition: 'top',
    });
  }
}
