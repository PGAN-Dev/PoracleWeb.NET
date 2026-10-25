import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { routes } from './app.routes';
import { SettingsService } from './core/services/settings.service';
import { ToastService } from './core/services/toast.service';

describe('app routes', () => {
  it('sends the old webhooks address to destinations', () => {
    const old = routes.find(r => r.path === 'admin/webhooks');
    expect(old?.redirectTo).toBe('admin/destinations');
    expect(old?.pathMatch).toBe('full');
  });

  it('serves the destinations page behind the admin guards', () => {
    const page = routes.find(r => r.path === 'admin/destinations');
    expect(page?.title).toBe('NAV.DESTINATIONS');
    expect(page?.canActivate?.length).toBe(2);
  });

  it('redirects /my-webhooks', () => {
    // A delegate's bookmark, and the exit route an impersonation session started before the rename recorded.
    const old = routes.find(r => r.path === 'my-webhooks');
    expect(old?.redirectTo).toBe('my-destinations');
    expect(old?.pathMatch).toBe('full');
  });

  it('serves My Destinations to any signed-in user', () => {
    const page = routes.find(r => r.path === 'my-destinations');
    expect(page?.title).toBe('NAV.MY_DESTINATIONS');
    expect(page?.canActivate?.length).toBe(1);
  });

  it('gates linked accounts behind the sign-in and disable_account_linking guards', () => {
    const page = routes.find(r => r.path === 'linked-accounts');
    expect(page?.canActivate?.length).toBe(2);
  });

  // Runs the route's own guard, so it fails if the route names a different key, or none.
  it('turns the linked accounts route away when disable_account_linking is on', async () => {
    localStorage.setItem('poracle_token', 'jwt');
    try {
      const asked: string[] = [];
      TestBed.configureTestingModule({
        providers: [
          {
            provide: SettingsService,
            useValue: {
              isDisabled: (key: string) => {
                asked.push(key);
                return key === 'disable_account_linking';
              },
              loadOnce: () => of([]),
            },
          },
          { provide: Router, useValue: { createUrlTree: () => 'dashboard' } },
          { provide: ToastService, useValue: { error: jest.fn() } },
          { provide: TranslateService, useValue: { instant: (k: string) => k } },
        ],
      });
      const page = routes.find(r => r.path === 'linked-accounts');
      const guard = page?.canActivate?.[1] as (route: ActivatedRouteSnapshot, state: RouterStateSnapshot) => Promise<unknown>;
      const result = await TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
      expect(asked).toEqual(['disable_account_linking']);
      expect(result).toBe('dashboard');
    } finally {
      localStorage.removeItem('poracle_token');
      TestBed.resetTestingModule();
    }
  });
});
