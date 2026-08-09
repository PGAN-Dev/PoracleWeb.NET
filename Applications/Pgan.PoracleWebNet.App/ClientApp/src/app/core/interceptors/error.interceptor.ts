import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { catchError, throwError } from 'rxjs';

import { ToastService } from '../services/toast.service';
import { TokenStoreService } from '../services/token-store.service';

/** Endpoints where errors should be silently swallowed (no user-facing toast). */
const SILENT_URL_PATTERNS = [
  '/api/config',
  '/api/masterdata',
  '/api/auth/me',
  '/api/auth/providers',
  '/api/admin/users/avatars',
  '/api/settings',
];

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
  const tokenStore = inject(TokenStoreService);
  const router = inject(Router);
  const translate = inject(TranslateService);

  return next(req).pipe(
    catchError(error => {
      const silent = shouldSilence(req.url);

      // On 401, clear token and redirect — but NOT during OAuth callback flow or login page
      if (error.status === 401 && !isAuthCallbackRoute()) {
        // The whole session, not just the access token. Three keys used to survive the app deciding the
        // session was invalid: poracle_admin_token -- the higher-privilege credential an impersonating
        // admin leaves behind, which stopImpersonating() would then install as the active token -- plus
        // the refresh token and its expiry, so the next load tried to refresh a session the server had
        // already rejected. Navigation is deliberately left alone: routing through AuthService.logout()
        // would append loggedout=1 and suppress the OIDC auto-redirect. See #616.
        localStorage.removeItem('poracle_token');
        localStorage.removeItem('poracle_admin_token');
        tokenStore.clear();
        // Preserve any existing query params (e.g. ?error=missing_required_role)
        const params = new URLSearchParams(window.location.search);
        router.navigate(['/login'], { queryParams: Object.fromEntries(params) });
      }

      // Messages come from HTTP_ERROR.*, which ToastService already uses and which is translated in
      // every locale. This interceptor used a parallel ERROR.* table carrying verbatim English in all
      // ten locales, so a German user saw an English toast for the same status. See #425.
      // Don't show toasts for silent endpoints
      if (!silent) {
        switch (error.status) {
          case 401:
            toast.error(translate.instant('HTTP_ERROR.UNAUTHORIZED'));
            break;
          case 403:
            // The backend tags "feature disabled" 403s by including a `disableKey` in the body
            // (RequireFeatureEnabledAttribute, FeatureDisabledExceptionFilter, TestAlertController).
            //
            // It used to redirect to /dashboard as well, on the assumption that such a 403 meant the page
            // itself was dead. Most of them mean nothing of the sort: they come from shared components
            // asking for something incidental -- the delivery preview inside every add-alarm dialog, a map
            // overlay -- and the redirect then moved the route out from under an open dialog, or bounced
            // the user off a page whose own feature was perfectly enabled. Navigation belongs to
            // disabledFeatureGuard, which knows which feature the route is for. See #515, #516.
            if (error.error?.disableKey) {
              toast.error(translate.instant('ERROR.FEATURE_DISABLED'));
            } else {
              toast.error(translate.instant('HTTP_ERROR.FORBIDDEN'));
            }
            break;
          case 404:
            toast.error(translate.instant('HTTP_ERROR.NOT_FOUND'));
            break;
          case 0:
            toast.error(translate.instant('HTTP_ERROR.NETWORK'));
            break;
          case 500:
            toast.error(translate.instant('HTTP_ERROR.SERVER_ERROR'));
            break;
          case 502:
          case 503:
          case 504:
            toast.error(translate.instant('HTTP_ERROR.UNAVAILABLE'));
            break;
        }
      }

      return throwError(() => error);
    }),
  );
};
