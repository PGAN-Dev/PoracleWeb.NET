import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AreaListComponent } from './area-list.component';
import { MuteService } from '../../core/services/mute.service';
import { PlacesService } from '../../core/services/places.service';
import { SettingsService } from '../../core/services/settings.service';

/**
 * A name the person still holds that PoracleNG has no fence for (a renamed or removed fence) was invisible,
 * could not be switched off, and was re-sent with every save. See #1060.
 *
 * These run through the real AreaService and the HTTP layer, because the guard that matters is what the API
 * and the service actually produce when PoracleNG's reads fail: `/api/areas/available` answers 200 `[]`,
 * `/api/areas/geofence` answers 200 `{ geofence: [] }`, and `AreaService.getGeofencePolygons` turns a failed
 * request into `[]`. None of them can emit an error the component could branch on.
 */
describe('AreaListComponent names Poracle no longer has', () => {
  interface Feeds {
    available?: { group?: string; name: string; userSelectable?: boolean }[];
    geofenceStatus?: number;
    polygons?: { displayName?: string; name: string }[];
  }

  const area = (name: string) => ({ name, group: 'City', userSelectable: true });
  const live = [area('Downtown'), area('Uptown')];
  const polygons = [{ name: 'Downtown' }, { name: 'Uptown' }];

  const setup = (selected: string[], feeds: Feeds = {}, deferPolygons = false) => {
    TestBed.resetTestingModule();
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
      ],
      imports: [AreaListComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      AREAS: {
        DEAD_AREAS: 'No longer available: {{names}}. They’ll be removed the next time you save.',
        DEAD_AREAS_REMOVE: 'Remove them',
        METHOD_AREAS_ACTIVE_ONE: '{{count}} area active',
        METHOD_AREAS_ACTIVE_OTHER: '{{count}} areas active',
        SNACK_SAVED_DROPPED: 'Saved. Left out, because Poracle has no such area: {{names}}',
        SNACK_UPDATED: 'Areas updated',
      },
    });
    translate.use('en');

    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AreaListComponent);
    fixture.detectChanges();

    const get = (suffix: string): TestRequest =>
      http.expectOne(r => r.method === 'GET' && r.url.endsWith(suffix) && !r.url.endsWith(`${suffix}/x`));
    get('/api/areas').flush(selected);
    get('/api/areas/available').flush(feeds.available ?? live);
    const geofence = get('/api/areas/geofence');
    const answerPolygons = () =>
      feeds.geofenceStatus && feeds.geofenceStatus >= 400
        ? geofence.flush('boom', { status: feeds.geofenceStatus, statusText: 'err' })
        : geofence.flush({ geofence: feeds.polygons ?? polygons, status: 'ok' });
    if (!deferPolygons) answerPolygons();
    fixture.detectChanges();

    const save = (answer: string[]) => {
      fixture.componentInstance.saveAreas();
      const put = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/api/areas'));
      const sent = (put.request.body as { areas: string[] }).areas;
      put.flush(answer);
      fixture.detectChanges();
      return sent;
    };
    return { answerPolygons, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, fixture, http, save };
  };

  it('shows the names once in a note', () => {
    const { el } = setup(['downtown', 'powhatan', 'swiftcreek']);

    expect(el.querySelector('.dead-areas-text')?.textContent?.trim()).toBe(
      'No longer available: powhatan, swiftcreek. They’ll be removed the next time you save.',
    );
    expect(el.querySelectorAll('mat-chip')).toHaveLength(1);
  });

  it('sends only live names on a save, and reports a plain save', () => {
    const { component, el, fixture, save } = setup(['downtown', 'powhatan']);
    const snackOpen = jest.spyOn(fixture.debugElement.injector.get(MatSnackBar), 'open');

    component.toggleAreaDirect(component.areas().find(a => a.name === 'Uptown')!);
    expect(save(['Downtown', 'Uptown'])).toEqual(['Downtown', 'Uptown']);

    expect(snackOpen.mock.calls[0][0]).toBe('Areas updated');
    expect(el.querySelector('.dead-areas')).toBeNull();
  });

  it('lets a person who switched everything else off save an empty selection instead of re-sending dead names', () => {
    const { component, save } = setup(['downtown', 'powhatan']);

    component.clearAllAreas();

    expect(save([])).toEqual([]);
  });

  it('shows no note when every held name is live', () => {
    const { el } = setup(['downtown', 'uptown']);

    expect(el.querySelector('.dead-areas')).toBeNull();
  });

  it('does not call the caller’s own drawn fence dead, even when it is missing from the available list', () => {
    const { el, save } = setup(['downtown', 'my_fence'], { polygons: [...polygons, { name: 'my_fence', displayName: 'My fence' }] });

    expect(el.querySelector('.dead-areas')).toBeNull();
    expect(save(['Downtown', 'my_fence'])).toEqual(['Downtown', 'my_fence']);
  });

  it('classifies nothing when both reads came back empty, as they do when PoracleNG’s reads fail', () => {
    const { component, el, save } = setup(['downtown', 'uptown', 'my_fence'], { available: [], polygons: [] });

    expect(el.querySelector('.dead-areas')).toBeNull();
    expect(component.selectedAreas()).toEqual(['downtown', 'uptown', 'my_fence']);
    expect(save(['downtown', 'uptown', 'my_fence'])).toEqual(['downtown', 'uptown', 'my_fence']);
  });

  it('classifies nothing when only the fence feed is empty', () => {
    const { el, save } = setup(['downtown', 'powhatan'], { polygons: [] });

    expect(el.querySelector('.dead-areas')).toBeNull();
    expect(save(['downtown', 'powhatan'])).toEqual(['Downtown', 'powhatan']);
  });

  it('classifies nothing when the fence feed request fails outright', () => {
    const { el, save } = setup(['downtown', 'powhatan'], { geofenceStatus: 500 });

    expect(el.querySelector('.dead-areas')).toBeNull();
    expect(save(['downtown', 'powhatan'])).toEqual(['Downtown', 'powhatan']);
  });

  it('renders the page before the fence feed answers, and adds the note when it does', () => {
    const { answerPolygons, el, fixture } = setup(['downtown', 'powhatan'], {}, true);

    expect(el.querySelector('.areas-page')).not.toBeNull();
    expect(el.querySelector('.dead-areas')).toBeNull();

    answerPolygons();
    fixture.detectChanges();

    expect(el.querySelector('.dead-areas-text')?.textContent).toContain('powhatan');
  });

  it('removes dead names on their own with the note’s action, without any other change', () => {
    const { el, fixture, http } = setup(['downtown', 'powhatan']);
    const button = el.querySelector('.dead-areas button') as HTMLButtonElement;

    expect(button.disabled).toBe(false);
    button.click();
    const put = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/api/areas'));
    expect((put.request.body as { areas: string[] }).areas).toEqual(['Downtown']);
    put.flush(['Downtown']);
    fixture.detectChanges();
    expect(el.querySelector('.dead-areas')).toBeNull();
    expect(fixture.componentInstance.hasChanges()).toBe(false);
  });

  it('leaves the note’s action off while other edits wait for the Save bar', () => {
    const { component, el, fixture } = setup(['downtown', 'powhatan']);

    component.toggleAreaDirect(component.areas().find(a => a.name === 'Uptown')!);
    fixture.detectChanges();

    expect((el.querySelector('.dead-areas button') as HTMLButtonElement).disabled).toBe(true);
  });
});
