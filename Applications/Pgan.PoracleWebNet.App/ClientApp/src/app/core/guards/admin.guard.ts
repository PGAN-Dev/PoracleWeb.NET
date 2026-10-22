import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ADMIN_RETURN_KEY } from './admin-return';
import { AuthService } from '../services/auth.service';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);
  const i18n = inject(I18nService);
  const returning = router.currentNavigation()?.extras.state?.[ADMIN_RETURN_KEY] === true;

  // Wait for user data to load before checking admin status
  const user = await auth.waitForUser();

  if (user?.isAdmin) {
    return true;
  }

  // A signed-in member (a webhook delegate following an admin's link, or an admin while viewing as
  // someone else) used to land on the dashboard with no word, and took the link for broken. With no
  // user the sign-in itself failed, and the session's own messages cover that.
  if (user && !returning) {
    toast.error(i18n.instant('ERROR.ADMIN_ONLY'));
  }

  return router.createUrlTree(['/dashboard']);
};
