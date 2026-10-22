import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AreaListComponent } from './area-list.component';
import { AreaService } from '../../core/services/area.service';
import { MuteService } from '../../core/services/mute.service';
import { PlacesService } from '../../core/services/places.service';
import { SettingsService } from '../../core/services/settings.service';

/**
 * The Areas card and the quiet chips speak about what is saved. Ticking an area and leaving without
 * saving used to read "2 areas active" on the card, and offered to quiet an area nothing was subscribed
 * to: Poracle had never heard of the subscription the card was describing.
 */
describe('AreaListComponent saved and unsaved selections', () => {
  const setup = () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        {
          provide: MuteService,
          useValue: { countdownLabel: () => '', refresh: jest.fn(), remainingFor: () => null },
        },
        {
          provide: PlacesService,
          useValue: { named: computed(() => []), load: jest.fn(() => of({ named: [] })), pin: computed(() => null) },
        },
        {
          provide: SettingsService,
          useValue: { isDisabled: (key: string) => key === 'disable_location', siteSettings: signal({ disable_location: 'true' }) },
        },
        {
          provide: AreaService,
          useValue: {
            getAvailable: () =>
              of([
                { name: 'Downtown', group: 'City', userSelectable: true },
                { name: 'Uptown', group: 'City', userSelectable: true },
              ]),
            getGeofencePolygons: () => of([]),
            getMapUrl: () => of(null),
            getSelected: () => of(['downtown']),
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      AREAS: {
        METHOD_AREAS_ACTIVE_ONE: '{{count}} area active',
        METHOD_AREAS_ACTIVE_OTHER: '{{count}} areas active',
        SNACK_FAILED: 'Failed to update areas',
        SNACK_NOT_PERMITTED: 'Not saved. These areas aren’t available to you: {{names}}',
        SNACK_SAVED_DROPPED: 'Saved. Left out, because Poracle has no such area: {{names}}',
        SNACK_UPDATED: 'Areas updated',
      },
      COMMON: { REMOVE_NAMED: 'Remove {{name}}' },
    });
    translate.use('en');

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const el = fixture.nativeElement as HTMLElement;
    const uptown = () => component.areas().find(a => a.name === 'Uptown')!;
    return { component, el, fixture, uptown };
  };

  it('counts only saved areas on the Areas card while a tick is unsaved', () => {
    const { component, el, fixture, uptown } = setup();

    component.toggleAreaDirect(uptown());
    fixture.detectChanges();

    expect(component.selectedAreas()).toHaveLength(2);
    expect(el.querySelector('.areas-status')?.textContent?.trim()).toBe('1 area active');
  });

  it('counts the new area once it is saved', () => {
    const { component, el, fixture, uptown } = setup();
    const areas = TestBed.inject(AreaService) as unknown as { update: jest.Mock };
    areas.update = jest.fn(() => of(['downtown', 'uptown']));

    component.toggleAreaDirect(uptown());
    component.saveAreas();
    fixture.detectChanges();

    expect(el.querySelector('.areas-status')?.textContent?.trim()).toBe('2 areas active');
    expect(component.hasChanges()).toBe(false);
  });

  /**
   * PUT /api/areas answers with the list PoracleNG holds after the write, and PoracleNG drops a name it has
   * no fence for without an error. The page used to keep the names it sent, so a dropped area read as
   * active, with a quiet chip, until the next reload.
   */
  it('keeps what PoracleNG kept, unticks what it dropped, and names it', () => {
    const { component, el, fixture, uptown } = setup();
    const areas = TestBed.inject(AreaService) as unknown as { update: jest.Mock };
    areas.update = jest.fn(() => of(['downtown']));
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.toggleAreaDirect(uptown());
    component.saveAreas();
    fixture.detectChanges();

    expect(component.savedAreas()).toEqual(['downtown']);
    expect(component.selectedAreas()).toEqual(['downtown']);
    expect(uptown().selected).toBe(false);
    expect(component.hasChanges()).toBe(false);
    expect(el.querySelector('.areas-status')?.textContent?.trim()).toBe('1 area active');
    expect(snackOpen.mock.calls[0][0]).toBe('Saved. Left out, because Poracle has no such area: Uptown');
  });

  it('says only "updated" when everything sent was kept, whatever case it comes back in', () => {
    const { component, fixture, uptown } = setup();
    const areas = TestBed.inject(AreaService) as unknown as { update: jest.Mock };
    areas.update = jest.fn(() => of(['downtown', 'uptown']));
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.toggleAreaDirect(uptown());
    component.saveAreas();

    expect(snackOpen.mock.calls[0][0]).toBe('Areas updated');
    expect(uptown().selected).toBe(true);
  });

  it('offers to quiet a saved area and not one that is only ticked', () => {
    const { component, el, fixture, uptown } = setup();
    component.viewMode.set('list');

    component.toggleAreaDirect(uptown());
    fixture.detectChanges();

    const rows = Array.from(el.querySelectorAll('.area-row'));
    const row = (name: string) => rows.find(r => r.querySelector('.area-row-name')?.textContent?.trim() === name)!;
    expect(row('Downtown').querySelector('app-quiet-chip')).not.toBeNull();
    expect(row('Uptown').querySelector('app-quiet-chip')).toBeNull();

    const chips = Array.from(el.querySelectorAll('.selected-chips mat-chip'));
    const chip = (name: string) => chips.find(c => c.textContent?.includes(name))!;
    expect(chip('Downtown').querySelector('app-quiet-chip')).not.toBeNull();
    expect(chip('Uptown').querySelector('app-quiet-chip')).toBeNull();
  });

  it('names the remove button on each area chip', () => {
    const { el } = setup();

    const remove = el.querySelector('.selected-chips button[matChipRemove]');
    expect(remove?.getAttribute('aria-label')).toBe('Remove Downtown');
  });

  /**
   * PUT /api/areas answers 400 `{ error, areas }` naming each area the user may not select, and nothing is
   * saved. "Failed to update areas" left them to guess which.
   */
  describe('a refused save', () => {
    const save = (failure: HttpErrorResponse) => {
      const ctx = setup();
      const areas = TestBed.inject(AreaService) as unknown as { update: jest.Mock };
      areas.update = jest.fn(() => throwError(() => failure));
      const snackOpen = jest.spyOn(ctx.fixture.debugElement.injector.get(MatSnackBar), 'open');
      ctx.component.toggleAreaDirect(ctx.uptown());
      ctx.component.saveAreas();
      return { ...ctx, snackOpen };
    };

    it('names the areas the server would not take, by their published names', () => {
      const { component, snackOpen } = save(
        new HttpErrorResponse({ error: { areas: ['uptown'], error: 'These areas are not available to you: uptown' }, status: 400 }),
      );

      expect(snackOpen.mock.calls[0][0]).toBe('Not saved. These areas aren’t available to you: Uptown');
      expect(component.savedAreas()).toEqual(['downtown']);
    });

    it('keeps its own words for a failure that names nothing', () => {
      const { snackOpen } = save(new HttpErrorResponse({ error: { error: 'PoracleNG could not be reached.' }, status: 503 }));

      expect(snackOpen.mock.calls[0][0]).toBe('Failed to update areas');
    });
  });
});
