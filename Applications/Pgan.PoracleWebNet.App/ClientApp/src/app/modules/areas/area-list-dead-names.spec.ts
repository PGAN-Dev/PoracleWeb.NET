import { provideHttpClient } from '@angular/common/http';
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
 * A name the person still holds that PoracleNG has no fence for (a renamed or removed fence) was invisible,
 * could not be switched off, and was re-sent with every save. Switch everything else off and the save sent
 * only those names, which left no areas behind a toast that read like a partial success. See #1060.
 */
describe('AreaListComponent names Poracle no longer has', () => {
  const setup = (selected: string[], polygons: { name: string; displayName?: string }[] = [], polygonsFail = false) => {
    TestBed.resetTestingModule();
    const update = jest.fn((names: string[]) => of(names));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: MuteService, useValue: { countdownLabel: () => '', refresh: jest.fn(), remainingFor: () => null } },
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
            getGeofencePolygons: () => (polygonsFail ? throwError(() => new Error('down')) : of(polygons)),
            getMapUrl: () => of(null),
            getSelected: () => of(selected),
            update,
          },
        },
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      AREAS: {
        DEAD_AREAS: 'No longer available: {{names}}. They’ll be removed the next time you save.',
        METHOD_AREAS_ACTIVE_ONE: '{{count}} area active',
        METHOD_AREAS_ACTIVE_OTHER: '{{count}} areas active',
        SNACK_SAVED_DROPPED: 'Saved. Left out, because Poracle has no such area: {{names}}',
        SNACK_UPDATED: 'Areas updated',
      },
    });
    translate.use('en');

    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, fixture, update };
  };

  it('shows the names once in a note', () => {
    const { el } = setup(['downtown', 'powhatan', 'swiftcreek']);

    const note = el.querySelector('.dead-areas');
    expect(note?.textContent?.trim()).toBe('No longer available: powhatan, swiftcreek. They’ll be removed the next time you save.');
    expect(el.querySelectorAll('mat-chip')).toHaveLength(1);
  });

  it('sends only live names on a save, and reports a plain save', () => {
    const { component, el, fixture, update } = setup(['downtown', 'powhatan']);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.toggleAreaDirect(component.areas().find(a => a.name === 'Uptown')!);
    component.saveAreas();
    fixture.detectChanges();

    expect(update).toHaveBeenCalledWith(['Downtown', 'Uptown']);
    expect(snackOpen.mock.calls[0][0]).toBe('Areas updated');
    expect(el.querySelector('.dead-areas')).toBeNull();
  });

  it('lets a person who switched everything else off save an empty selection instead of re-sending dead names', () => {
    const { component, update } = setup(['downtown', 'powhatan']);

    component.clearAllAreas();
    component.saveAreas();

    expect(update).toHaveBeenCalledWith([]);
  });

  it('shows no note when every held name is live', () => {
    const { el } = setup(['downtown', 'uptown']);

    expect(el.querySelector('.dead-areas')).toBeNull();
  });

  it('does not call the caller’s own drawn fence dead', () => {
    const { component, el, update } = setup(['downtown', 'my_fence'], [{ name: 'my_fence', displayName: 'My fence' }]);

    expect(el.querySelector('.dead-areas')).toBeNull();
    component.saveAreas();
    expect(update).toHaveBeenCalledWith(['Downtown', 'my_fence']);
  });

  it('judges nothing dead when the fence list could not be read', () => {
    const { component, el, update } = setup(['downtown', 'my_fence'], [], true);

    expect(el.querySelector('.dead-areas')).toBeNull();
    component.saveAreas();
    expect(update).toHaveBeenCalledWith(['Downtown', 'my_fence']);
  });
});
