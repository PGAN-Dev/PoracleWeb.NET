import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { SettingsService } from '../services/settings.service';
import { ToastService } from '../services/toast.service';

/**
 * Blocks navigation to a route when the matching `disable_*` site setting is true and
 * redirects to /dashboard with a toast. Pairs with the `RequireFeatureEnabledAttribute`
 * resource filter on the matching backend controller — without this guard, a user typing
 * the URL directly would load the page and watch every API call return 403. See #236.
 */
export function disabledFeatureGuard(disableKey: string): CanActivateFn {
  return async () => {
    const settings = inject(SettingsService);
    const router = inject(Router);
    const toast = inject(ToastService);
    const translate = inject(TranslateService);

    // loadOnce is idempotent; if the cache is warm it emits and completes immediately.
    // We await it because authGuard runs first but doesn't itself wait on settings.
    // If the settings endpoint fails (network blip, 5xx) we DON'T want to block navigation
    // forever — the backend still enforces the gate and the 403 interceptor will redirect
    // the user if they hit a disabled endpoint. Allow navigation so the user isn't stuck. (#236)
    try {
      await firstValueFrom(settings.loadOnce());
    } catch {
      return true;
    }

    if (settings.isDisabled(disableKey)) {
      toast.error(translate.instant('ERROR.FEATURE_DISABLED'));
      return router.createUrlTree(['/dashboard']);
    }
    return true;
  };
}
