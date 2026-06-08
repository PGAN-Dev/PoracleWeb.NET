import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { TokenStoreService } from '../services/token-store.service';

/**
 * Endpoints that must never trigger a refresh — the refresh/login calls themselves (recursion
 * guard) and the anonymous providers probe.
 */
const SKIP_PATHS = ['/api/auth/oidc/refresh', '/api/auth/oidc/login', '/api/auth/providers'];

/**
 * Silent OIDC session renewal. Registered closest to the backend so it sees a 401 before the error
 * interceptor redirects. Two paths:
 *  - Proactive: when the JWT is about to expire, refresh first, then send with the fresh bearer.
 *  - Reactive: on a 401, refresh once and retry the request.
 * No-ops entirely when there's no refresh token (Discord/Telegram/local logins, or OIDC without
 * refresh) — those keep the existing "401 → logout" behavior via the error interceptor.
 */
export const oidcRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(TokenStoreService);

  const isApi = req.url.includes('/api/');
  const skip = SKIP_PATHS.some(path => req.url.includes(path));

  if (!isApi || skip || !store.hasRefreshToken()) {
    return next(req);
  }

  const withBearer = (token: string) => req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  // Proactive refresh — token is within the expiry skew window.
  if (store.isExpiringSoon()) {
    return store.refresh().pipe(
      switchMap(token => next(withBearer(token))),
      // If the proactive refresh fails, still let the request go; a 401 will surface and the
      // failed refresh has already emitted forceLogout$.
      catchError(() => next(req)),
    );
  }

  // Reactive refresh — retry once after a 401.
  return next(req).pipe(
    catchError(err => {
      if (err.status === 401 && store.hasRefreshToken()) {
        return store.refresh().pipe(
          switchMap(token => next(withBearer(token))),
          catchError(refreshErr => throwError(() => refreshErr)),
        );
      }

      return throwError(() => err);
    }),
  );
};
