import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { disabledFeatureGuard } from './disabled-feature.guard';
import { SettingsService } from '../services/settings.service';
import { ToastService } from '../services/toast.service';

describe('disabledFeatureGuard', () => {
  let settings: { isDisabled: jest.Mock; loadOnce: jest.Mock };
  let router: { createUrlTree: jest.Mock };
  let toast: { error: jest.Mock };
  let translate: { instant: jest.Mock };
  const mockRoute = {} as ActivatedRouteSnapshot;
  const mockState = {} as RouterStateSnapshot;

  beforeEach(() => {
    settings = { isDisabled: jest.fn(), loadOnce: jest.fn().mockReturnValue(of([])) };
    router = { createUrlTree: jest.fn().mockReturnValue('dashboard-url-tree' as unknown as UrlTree) };
    toast = { error: jest.fn() };
    translate = { instant: jest.fn().mockImplementation((k: string) => k) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: SettingsService, useValue: settings },
        { provide: Router, useValue: router },
        { provide: ToastService, useValue: toast },
        { provide: TranslateService, useValue: translate },
      ],
    });
  });

  it('allows navigation when the feature is enabled', async () => {
    settings.isDisabled.mockReturnValue(false);
    const guard = disabledFeatureGuard('disable_mons');

    const result = await TestBed.runInInjectionContext(() => guard(mockRoute, mockState));

    expect(result).toBe(true);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('redirects to dashboard with a toast when the feature is disabled', async () => {
    settings.isDisabled.mockReturnValue(true);
    const guard = disabledFeatureGuard('disable_mons');

    const result = await TestBed.runInInjectionContext(() => guard(mockRoute, mockState));

    expect(toast.error).toHaveBeenCalledWith('ERROR.FEATURE_DISABLED');
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(result).toBe('dashboard-url-tree');
  });

  it('awaits loadOnce so a guard fired before settings load reads accurate state', async () => {
    // Ensures the guard doesn't false-allow under a race where it fires before app init has populated
    // the siteSettings signal — without the await, isDisabled() reads {} and returns false. (#236)
    settings.isDisabled.mockReturnValue(true);
    const guard = disabledFeatureGuard('disable_raids');

    await TestBed.runInInjectionContext(() => guard(mockRoute, mockState));

    expect(settings.loadOnce).toHaveBeenCalled();
  });

  it('falls back to allowing navigation when loadOnce errors', async () => {
    // If the settings endpoint flakes the user must not get stuck — the backend still enforces
    // the gate and the 403 interceptor will redirect them if they hit a disabled endpoint.
    settings.loadOnce.mockReturnValue(throwError(() => new Error('Network error')));
    const guard = disabledFeatureGuard('disable_mons');

    const result = await TestBed.runInInjectionContext(() => guard(mockRoute, mockState));

    expect(result).toBe(true);
    expect(settings.isDisabled).not.toHaveBeenCalled();
  });
});
