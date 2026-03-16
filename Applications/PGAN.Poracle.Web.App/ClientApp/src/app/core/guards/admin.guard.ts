import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Wait for user data to load before checking admin status
  const user = await auth.waitForUser();

  if (user?.isAdmin) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
