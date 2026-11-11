import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../services/auth.service';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);
  const i18n = inject(I18nService);

  // If there's a token, wait for user to load
  if (auth.isAuthenticated()) {
    const user = await auth.waitForUserWithRetry();
    if (user) return true;

    // /api/auth/me could not be answered (network error, 429, 5xx): that says nothing about the token, and
    // sending the member to the sign-in page for it was a bounce a reload undid. Only a 401 means signed out.
    if (auth.sessionUnavailable() && auth.isAuthenticated()) {
      toast.error(i18n.instant('ERROR.SESSION_UNAVAILABLE'));
      auth.retrySessionInBackground();
      return true;
    }
  }

  return router.createUrlTree(['/login']);
};
