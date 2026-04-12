import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { catchError, throwError } from 'rxjs';

import { ToastService } from '../services/toast.service';

/** Endpoints where errors should be silently swallowed (no user-facing toast). */
const SILENT_URL_PATTERNS = ['/api/config', '/api/masterdata', '/api/auth/me', '/api/admin/users/avatars', '/api/settings'];

function shouldSilence(url: string): boolean {
  return SILENT_URL_PATTERNS.some(pattern => url.includes(pattern));
}

/** Routes where 401s should NOT trigger a redirect to /login (e.g. OAuth callback, login page itself). */
function isAuthCallbackRoute(): boolean {
  return (
    window.location.pathname.includes('/auth/') || window.location.hash.includes('token=') || window.location.pathname.endsWith('/login')
  );
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const router = inject(Router);
  const translate = inject(TranslateService);

  return next(req).pipe(
    catchError(error => {
      const silent = shouldSilence(req.url);

      // On 401, clear token and redirect — but NOT during OAuth callback flow or login page
      if (error.status === 401 && !isAuthCallbackRoute()) {
        localStorage.removeItem('poracle_token');
        // Preserve any existing query params (e.g. ?error=missing_required_role)
        const params = new URLSearchParams(window.location.search);
        router.navigate(['/login'], { queryParams: Object.fromEntries(params) });
      }

      // Don't show toasts for silent endpoints
      if (!silent) {
        switch (error.status) {
          case 401:
            toast.error(translate.instant('ERROR.SESSION_EXPIRED'));
            break;
          case 403:
            toast.error(translate.instant('ERROR.PERMISSION_DENIED'));
            break;
          case 404:
            toast.error(translate.instant('ERROR.NOT_FOUND'));
            break;
          case 0:
            toast.error(translate.instant('ERROR.NETWORK'));
            break;
          case 500:
            toast.error(translate.instant('ERROR.GENERIC'));
            break;
          case 502:
          case 503:
          case 504:
            toast.error(translate.instant('ERROR.SERVER_UNAVAILABLE'));
            break;
        }
      }

      return throwError(() => error);
    }),
  );
};
