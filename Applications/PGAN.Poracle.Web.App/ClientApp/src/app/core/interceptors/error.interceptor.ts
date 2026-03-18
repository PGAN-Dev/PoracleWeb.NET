import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { ToastService } from '../services/toast.service';

/** Endpoints where errors should be silently swallowed (no user-facing toast). */
const SILENT_URL_PATTERNS = ['/api/config', '/api/masterdata', '/api/auth/me', '/api/admin/users/avatars'];

function shouldSilence(url: string): boolean {
  return SILENT_URL_PATTERNS.some(pattern => url.includes(pattern));
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const router = inject(Router);

  return next(req).pipe(
    catchError(error => {
      const silent = shouldSilence(req.url);

      // Always clear token and redirect on 401, even for silent endpoints
      if (error.status === 401) {
        localStorage.removeItem('poracle_token');
        router.navigate(['/login']);
      }

      // Don't show toasts or redirect for silent endpoints
      if (!silent) {
        switch (error.status) {
          case 401:
            toast.error('Session expired. Please log in again.');
            break;
          case 403:
            toast.error("You don't have permission for this action.");
            break;
          case 404:
            toast.error('The requested resource was not found.');
            break;
          case 0:
            toast.error('Network error. Check your connection.');
            break;
          case 500:
            toast.error('Something went wrong. Please try again.');
            break;
          case 502:
          case 503:
          case 504:
            toast.error('Server is temporarily unavailable.');
            break;
        }
      }

      return throwError(() => error);
    }),
  );
};
