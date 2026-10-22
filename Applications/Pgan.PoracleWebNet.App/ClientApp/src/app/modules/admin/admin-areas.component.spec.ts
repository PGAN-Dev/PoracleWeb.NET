import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AdminAreasComponent } from './admin-areas.component';
import { AdminArea, AdminAreaService } from '../../core/services/admin-area.service';

const area = (name: string, hidden = false): AdminArea => ({ hidden, hiddenInKoji: false, name, group: '' }) as AdminArea;

/** Staged hides are real work on a 1,236-row list; leaving the page must not drop them without a word. */
describe('AdminAreasComponent unsaved changes', () => {
  let setHidden: jest.Mock;

  const create = (saveFails = false) => {
    setHidden = jest.fn().mockReturnValue(saveFails ? throwError(() => new Error('nope')) : of({ reloaded: true, saved: true }));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        {
          provide: AdminAreaService,
          useValue: { setHidden, getAreas: () => of({ areas: [area('north'), area('south', true)], orphaned: [] }) },
        },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
      ],
      imports: [AdminAreasComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AdminAreasComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  const row = (sut: AdminAreasComponent, name: string) => sut.areas().find(a => a.name === name)!;

  it('reports none on a freshly loaded page', () => {
    expect(create().hasUnsavedChanges()).toBe(false);
  });

  it('reports a staged hide', () => {
    const sut = create();
    sut.toggle(row(sut, 'north'), true);

    expect(sut.hasUnsavedChanges()).toBe(true);
  });

  it('reports none when a toggle is put back by hand', () => {
    const sut = create();
    sut.toggle(row(sut, 'north'), true);
    sut.toggle(row(sut, 'north'), false);

    expect(sut.hasUnsavedChanges()).toBe(false);
  });

  it('reports none once saved', () => {
    const sut = create();
    sut.toggle(row(sut, 'north'), true);
    sut.save();

    expect(setHidden).toHaveBeenCalled();
    expect(sut.hasUnsavedChanges()).toBe(false);
  });

  it('still reports the change when the save failed', () => {
    const sut = create(true);
    sut.toggle(row(sut, 'north'), true);
    sut.save();

    expect(sut.hasUnsavedChanges()).toBe(true);
  });

  it('reports none once discarded', () => {
    const sut = create();
    sut.toggle(row(sut, 'north'), true);
    sut.discard();

    expect(sut.hasUnsavedChanges()).toBe(false);
  });

  it('asks the browser to confirm a reload only while something is unsaved', () => {
    const sut = create();
    const clean = new Event('beforeunload', { cancelable: true });
    sut.onBeforeUnload(clean);
    expect(clean.defaultPrevented).toBe(false);

    sut.toggle(row(sut, 'north'), true);
    const dirty = new Event('beforeunload', { cancelable: true });
    sut.onBeforeUnload(dirty);
    expect(dirty.defaultPrevented).toBe(true);
  });
});

/** 1,235 rows each said "Hidden" beside an off switch. The word belongs over the column, once. */
describe('AdminAreasComponent column header', () => {
  const render = () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        {
          provide: AdminAreaService,
          useValue: { setHidden: jest.fn(), getAreas: () => of({ areas: [area('north'), area('south', true)], orphaned: [] }) },
        },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
      ],
      imports: [AdminAreasComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AdminAreasComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('labels the switch column once, in a header row', () => {
    const el = render();

    expect(el.querySelectorAll('[data-test="rows-head"]')).toHaveLength(1);
    expect(el.querySelector('[data-test="rows-head"]')?.textContent).toContain('ADMIN_AREAS.HIDE');
  });

  it('puts no label beside each switch, and keeps each one named for screen readers', () => {
    const toggles = Array.from(render().querySelectorAll('mat-slide-toggle'));

    expect(toggles).toHaveLength(2);
    for (const toggle of toggles) {
      expect(toggle.textContent?.trim()).toBe('');
      // The input, not the host attribute: MatSlideToggle names its inner switch from the input only.
      expect(toggle.querySelector('button[role="switch"]')?.getAttribute('aria-label')).toBe('ADMIN_AREAS.HIDE_ARIA');
    }
  });
});
