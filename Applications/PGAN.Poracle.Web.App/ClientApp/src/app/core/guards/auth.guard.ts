import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // If there's a token, wait for user to load
  if (auth.isAuthenticated()) {
    const user = await auth.waitForUser();
    if (user) return true;
  }

  return router.createUrlTree(['/login']);
};
