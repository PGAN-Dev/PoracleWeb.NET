import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom, isObservable, of } from 'rxjs';

import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  let dialog: { open: jest.Mock };

  const run = (component: HasUnsavedChanges | null) =>
    TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(
        component as HasUnsavedChanges,
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        {} as RouterStateSnapshot,
      ),
    );

  const resolve = async (result: ReturnType<typeof run>) => (isObservable(result) ? firstValueFrom(result as Observable<boolean>) : result);

  function setup(answer: unknown) {
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(answer) }) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialog, useValue: dialog },
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
});

describe('unsavedChangesGuard wiring', () => {
  // The guard does nothing until a route names it; the audit found neither page had one.
  it.each(['admin/areas', 'admin/settings'])('guards %s', async path => {
    const { routes } = await import('../../app.routes');
    const route = routes.find(r => r.path === path);

    expect(route?.canDeactivate).toContain(unsavedChangesGuard);
  });
});
