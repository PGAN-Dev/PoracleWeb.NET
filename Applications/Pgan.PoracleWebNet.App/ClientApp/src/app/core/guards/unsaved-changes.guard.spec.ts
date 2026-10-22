import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';

import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';
import { TokenStoreService } from '../services/token-store.service';

describe('unsavedChangesGuard', () => {
  let dialog: { open: jest.Mock };

  const run = (component: HasUnsavedChanges | null, target = '/admin/users') =>
    TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(
        component as HasUnsavedChanges,
        {} as ActivatedRouteSnapshot,
        { url: '/admin/settings' } as RouterStateSnapshot,
        { url: target } as RouterStateSnapshot,
      ),
    );

  beforeEach(() => localStorage.setItem('poracle_token', 'jwt'));
  afterEach(() => localStorage.removeItem('poracle_token'));

  const resolve = async (result: ReturnType<typeof run>) => (isObservable(result) ? firstValueFrom(result as Observable<boolean>) : result);

  function setup(answer: unknown) {
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(answer) }) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialog, useValue: dialog },
        { provide: TokenStoreService, useValue: { getAccessToken: () => localStorage.getItem('poracle_token') } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
  }

  it('lets a clean page go without asking', async () => {
    setup(true);

    expect(await resolve(run({ hasUnsavedChanges: () => false }))).toBe(true);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('lets a page go when there is no component to ask', async () => {
    setup(true);

    expect(await resolve(run(null))).toBe(true);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('asks before leaving unsaved changes, and leaves when told to', async () => {
    setup(true);

    expect(await resolve(run({ hasUnsavedChanges: () => true }))).toBe(true);
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });

  it('stays when the admin cancels', async () => {
    setup(false);

    expect(await resolve(run({ hasUnsavedChanges: () => true }))).toBe(false);
  });

  it('stays when the dialog is dismissed without an answer', async () => {
    setup(undefined);

    expect(await resolve(run({ hasUnsavedChanges: () => true }))).toBe(false);
  });

  it('lets a dirty page go without asking once the session is over', async () => {
    // Every session-ending path drops the token before it navigates; there is nothing left to save to,
    // and "Stay" would strand the user on a page whose every request 401s.
    setup(false);
    localStorage.removeItem('poracle_token');

    expect(await resolve(run({ hasUnsavedChanges: () => true }))).toBe(true);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('lets a dirty page go to the login page without asking', async () => {
    setup(false);

    expect(await resolve(run({ hasUnsavedChanges: () => true }, '/login?loggedout=1'))).toBe(true);
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('still asks for a route that merely starts with "login"', async () => {
    setup(false);

    expect(await resolve(run({ hasUnsavedChanges: () => true }, '/loginhelp'))).toBe(false);
    expect(dialog.open).toHaveBeenCalledTimes(1);
  });
});

describe('unsavedChangesGuard wiring', () => {
  // The guard does nothing until a route names it; the audit found neither page had one.
  it.each(['admin/areas', 'admin/settings'])('guards %s', async path => {
    const { routes } = await import('../../app.routes');
    const route = routes.find(r => r.path === path);

    expect(route?.canDeactivate).toContain(unsavedChangesGuard);
  });
});

/** The real router, the real guard and the real session-ending call the 401 path makes. */
describe('unsavedChangesGuard with the router', () => {
  @Component({ template: '' })
  class DirtyPageComponent implements HasUnsavedChanges {
    hasUnsavedChanges(): boolean {
      return true;
    }
  }

  @Component({ template: '' })
  class BlankComponent {}

  let dialog: { open: jest.Mock };

  beforeEach(() => {
    localStorage.setItem('poracle_token', 'jwt');
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(false) }) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { canDeactivate: [unsavedChangesGuard], component: DirtyPageComponent, path: 'admin/settings' },
          { component: BlankComponent, path: 'admin/users' },
          { component: BlankComponent, path: 'login' },
        ]),
        { provide: MatDialog, useValue: dialog },
        { provide: TokenStoreService, useValue: { getAccessToken: () => localStorage.getItem('poracle_token') } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
  });

  afterEach(() => localStorage.removeItem('poracle_token'));

  const settle = () => new Promise(resolve => setTimeout(resolve));

  it('a 401 ending the session reaches /login with edits staged, without a dialog', async () => {
    const harness = await RouterTestingHarness.create('/admin/settings');
    const router = TestBed.inject(Router);

    // What error.interceptor does on a 401: drop the tokens, then navigate.
    localStorage.removeItem('poracle_token');
    await harness.navigateByUrl('/login');
    await settle();

    expect(router.url).toBe('/login');
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('an ordinary navigation away with edits staged still asks, and Stay stays', async () => {
    const harness = await RouterTestingHarness.create('/admin/settings');
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/admin/users');
    await settle();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(router.url).toBe('/admin/settings');
    expect(harness).toBeTruthy();
  });
});
