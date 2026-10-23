import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { catchError, throwError } from 'rxjs';

import { ADMIN_RETURN_STATE } from '../guards/admin-return';
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
  // RuleRepairService turns the expected 409 (a run is already going) into the status it carries, and the
  // card words every other failure itself.
  '/api/admin/rule-repair',
  // MuteService owns its own messaging, including the benign 404 from resuming a quiet period that
  // had already lapsed -- the store expires entries itself, so that is the ordinary case.
  '/api/mutes',
  // TestAlertService words every outcome itself (429, 404, 501's server reason, feature disabled). With
  // the interceptor also toasting, the two snackbars raced and a failed test read "An unexpected server
  // error occurred" instead of saying the test alert failed.
  '/api/test-alert',
  // PlayerAccountService words every outcome itself: 409 and 422 carry a sentence from the server, and a
  // coded 403 means self-service is off. The interceptor's generic toast would hide all of that.
  '/api/accounts',
  '/api/admin/accounts',
  // BuildInfoService treats a failed version check as "no label"; it is cosmetic and runs on every page load.
  '/api/version',
  // Polygons only shade the map in an alarm dialog's Delivery tab (and the dashboard's overview); every
  // reader falls back to drawing nothing, so a failure is not worth a "server error" over an unsaved form.
  '/api/areas/geofence',
  // The display-name read behind every scope chip (UserGeofenceService.getCustomGeofenceNames). Chips fall
  // back to the stored name; the geofence page's own read of this path stays loud.
  '/api/geofences/custom?names=1',
  // Pre-checks before a destructive action (clearing the pin, deleting a place). Their callers fall back
  // to a plainer confirm when the read fails, so a toast would report a failure nobody needs to act on.
  '/api/location/pin-usage',
  '/api/location/place-usage',
  // The pin card's static map and the weather row. Both callers draw nothing when the read fails, and a
  // 404 on the first read after a restart toasted "not found" over an Areas page that worked.
  '/api/location/staticmap',
  '/api/location/weather',
];

/** The token a request carried, from the header authInterceptor (or the refresh retry) attached. */
function bearerOf(header: string | null): string | null {
  const match = /^Bearer\s+(.+)$/i.exec(header ?? '');
  return match?.[1]?.trim() || null;
}

function shouldSilence(url: string): boolean {
  return SILENT_URL_PATTERNS.some(pattern => url.includes(pattern));
}

/** The statuses the interceptor toasts on its own (outside the 401 path), whatever the caller does next. */
const TOASTED_STATUSES = new Set([0, 403, 404, 500, 502, 503, 504]);

/**
 * True when the interceptor has already told the user about this failure. A caller that would word the
 * same failure again (an add dialog's inline note, say) checks this so the user reads one message, not two.
 */
export function reportedByInterceptor(err: unknown): boolean {
  const failure = err as { status?: unknown; url?: unknown } | null | undefined;
  if (typeof failure?.status !== 'number' || !TOASTED_STATUSES.has(failure.status)) return false;
  return typeof failure.url !== 'string' || !shouldSilence(failure.url);
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

  // The two session messages explain a navigation, and on a reload they land before the translations do:
  // instant() then answers the key itself, so the whole toast read "HTTP_ERROR.UNAUTHORIZED". get() waits
  // for the language to load.
  const sayTranslated = (key: string) => {
    const now = translate.instant(key);
    if (now !== key || typeof translate.get !== 'function') {
      toast.error(now);
      return;
    }
    translate.get(key).subscribe(message => toast.error(message));
  };

  return next(req).pipe(
    catchError(error => {
      const silent = shouldSilence(req.url);

      // On 401, clear token and redirect — but NOT during OAuth callback flow or login page
      if (error.status === 401 && !isAuthCallbackRoute()) {
        // A page load sends about nine requests at once, so a session that dies takes all of them with it
        // and their 401s arrive together. Each is judged against the token it was *sent with*: once the
        // first has ended or replaced that session, the rest are about a session that no longer exists.
        // Judging them against storage instead let the second 401 of a revoked delegate's burst find the
        // stash already spent and sign out someone who had just been returned to their own account, and
        // gave every 401 after the first its own "session expired" toast. A request sent with no token
        // at all has no session to end either: the one it would have belonged to is already over.
        const retried = tokenStore.retriedTokenFor(error);
        const sentWith = retried !== undefined ? retried : bearerOf(req.headers.get('Authorization'));
        const current = tokenStore.getAccessToken()?.trim() || null;
        if (!sentWith || sentWith !== current) {
          return throwError(() => error);
        }

        // A 401 while inspecting another account is that account's problem, not the admin's, so end the
        // inspection rather than the session. Without this, inspecting a blocked or deleted user hit the
        // session-ending path below and signed the admin out with nothing to return to. See #706.
        // Toasted even for the silenced endpoints: unlike a background poll, this one explains a
        // navigation the admin can see happen.
        if (tokenStore.tryRestoreAdminSession()) {
          sayTranslated('HTTP_ERROR.INSPECTION_ENDED');
          router.navigate([tokenStore.takeImpersonationReturn()], { state: ADMIN_RETURN_STATE });
          return throwError(() => error);
        }

        // The whole session, not just the access token. Three keys used to survive the app deciding the
        // session was invalid: poracle_admin_token -- the higher-privilege credential an impersonating
        // admin leaves behind, which stopImpersonating() would then install as the active token -- plus
        // the refresh token and its expiry, so the next load tried to refresh a session the server had
        // already rejected. Navigation deliberately skips AuthService.logout(), which would append
        // loggedout=1 and suppress the OIDC auto-redirect. See #616.
        // The tokens go now and the user once the login page is up: forgetting the user swaps the shell's
        // layout, and doing that on the signed-in route rebuilt the page with no token. Toasted whichever
        // request found out, because which of a burst lands first is up to the network, and a silent one
        // landing first used to leave the user on the login page with no word of why.
        const params = new URLSearchParams(window.location.search);
        tokenStore.endSession(() => router.navigate(['/login'], { queryParams: Object.fromEntries(params) }));
        sayTranslated('HTTP_ERROR.UNAUTHORIZED');
        return throwError(() => error);
      }

      // Messages come from HTTP_ERROR.*, which ToastService already uses and which is translated in
      // every locale. This interceptor used a parallel ERROR.* table carrying verbatim English in all
      // ten locales, so a German user saw an English toast for the same status. See #425.
      // Don't show toasts for silent endpoints
      if (!silent && TOASTED_STATUSES.has(error.status)) {
        switch (error.status) {
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
