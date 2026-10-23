import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { ScopePickerComponent } from './scope-picker.component';
import { ConfigService } from '../../../core/services/config.service';
import { AlarmScope } from '../../utils/alarm-scope';

describe('ScopePickerComponent', () => {
  let fixture: ComponentFixture<ScopePickerComponent>;
  let ref: ComponentRef<ScopePickerComponent>;

  function create(scope: AlarmScope, inputs: Record<string, unknown> = {}): ScopePickerComponent {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
      imports: [ScopePickerComponent],
    });
    fixture = TestBed.createComponent(ScopePickerComponent);
    ref = fixture.componentRef;
    ref.setInput('scope', scope);
    for (const [name, value] of Object.entries(inputs)) ref.setInput(name, value);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('opens on the scope the host passed, not its own default', () => {
    // Seeding in the constructor read the model default instead of the input, then wrote it straight
    // back — silently discarding an alarm's real scope when editing it, and the Alert Defaults
    // preference when creating one. A signal input is not populated until after construction.
    const picker = create({ distanceKm: 3, mode: 'place', placeLabel: 'work' });

    expect(picker.mode()).toBe('near');
    expect(picker.placeLabel()).toBe('work');
    expect(picker.distanceKm()).toBe(3);
    expect(picker.scope()).toEqual({ distanceKm: 3, mode: 'place', placeLabel: 'work' });
  });

  it('opens on the inherited scope without inventing a radius', () => {
    const picker = create({ mode: 'profile' });

    expect(picker.mode()).toBe('inherit');
    expect(picker.scope()).toEqual({ mode: 'profile' });
  });

  it('opens on the areas an alarm is confined to', () => {
    const picker = create({ areas: ['terrigal'], mode: 'areas' });

    expect(picker.mode()).toBe('areas');
    expect(picker.selectedAreas()).toEqual(['terrigal']);
  });

  it('reads a bare radius as measured from the pin', () => {
    const picker = create({ distanceKm: 2, mode: 'profile' });

    expect(picker.mode()).toBe('near');
    expect(picker.placeLabel()).toBe('');
  });

  it('warns only when measuring from a pin that is not set', () => {
    expect(create({ distanceKm: 2, mode: 'profile' }).pinMissing()).toBe(true);
    expect(create({ mode: 'profile' }).pinMissing()).toBe(false);
    expect(create({ areas: ['terrigal'], mode: 'areas' }).pinMissing()).toBe(false);
  });
  /**
   * The list is the areas you subscribe to, not every area on the server. The control sits under
   * "Anywhere in my areas" and reads "Only in specific areas": it narrows within what you already get,
   * so offering the whole instance turns it into a second, hidden way to subscribe. On a multi-community
   * server that is hundreds of areas from cities the reader has nothing to do with. See #873.
   */
  describe('the areas it offers', () => {
    function answer(scope: AlarmScope, available: string[], selected: string[]): ScopePickerComponent {
      const picker = create(scope);
      const http = TestBed.inject(HttpTestingController);

      http.expectOne('http://test/api/areas/available').flush(available.map(name => ({ name, group: 'g', userSelectable: true })));
      http.expectOne('http://test/api/areas').flush(selected);
      http.match(() => true).forEach(r => r.flush([]));
      fixture.detectChanges();

      return picker;
    }

    it('offers only the areas the user subscribes to', () => {
      const picker = answer({ areas: [], mode: 'areas' }, ['Mechanicsville', 'Aliamanu', 'Aksarben'], ['mechanicsville']);

      expect(picker.availableAreas().map(a => a.name)).toEqual(['Mechanicsville']);
    });

    /**
     * The tightening trap. A rule scoped to an area its owner has since unsubscribed from must keep
     * that option, or the checkbox backing it vanishes and the area drops off the rule on the next
     * save. One rule in production is in exactly this state.
     */
    it('keeps an area the rule already carries even when it is no longer subscribed', () => {
      const picker = answer({ areas: ['aliamanu'], mode: 'areas' }, ['Mechanicsville', 'Aliamanu'], ['mechanicsville']);

      expect(
        picker
          .availableAreas()
          .map(a => a.name)
          .sort(),
      ).toEqual(['Aliamanu', 'Mechanicsville']);
    });

    it('matches on case, because subscriptions are stored lowercased', () => {
      const picker = answer({ areas: [], mode: 'areas' }, ['Bon Air - Robious'], ['bon air - robious']);

      expect(picker.availableAreas().map(a => a.name)).toEqual(['Bon Air - Robious']);
    });

    it('lists a drawn fence once when both the subscribed and the own lists carry it', () => {
      const picker = create({ areas: [], mode: 'areas' });
      const http = TestBed.inject(HttpTestingController);

      http.expectOne('http://test/api/areas/available').flush([
        { name: 'ikea lot', group: 'g', userSelectable: false },
        { name: 'Aliamanu', group: 'g', userSelectable: true },
      ]);
      http.expectOne('http://test/api/areas').flush(['ikea lot', 'aliamanu']);
      http.expectOne('http://test/api/geofences/custom').flush([{ displayName: 'IKEA Lot', kojiName: 'ikea lot' }]);
      http.match(() => true).forEach(r => r.flush([]));
      fixture.detectChanges();

      expect(picker.availableAreas()).toEqual([
        { name: 'ikea lot', own: true },
        { name: 'Aliamanu', own: false },
      ]);
    });

    it('offers nothing from the instance when the user subscribes to nothing', () => {
      const picker = answer({ areas: [], mode: 'areas' }, ['Mechanicsville', 'Aliamanu'], []);

      expect(picker.availableAreas().filter(a => !a.own)).toEqual([]);
    });
  });

  /**
   * The radius field used to be checked by the browser alone: 500 outlined it red with no message, Save
   * stayed enabled, and the alarm was stored at 500 km. The picker now says what is wrong and tells the
   * host, which keeps Save off while it is.
   */
  describe('the radius it accepts', () => {
    function errorText(): string {
      return (fixture.nativeElement as HTMLElement).querySelector('mat-error')?.textContent?.trim() ?? '';
    }

    it('refuses a radius past 100 km, says so, and tells the host', () => {
      const picker = create({ distanceKm: 1, mode: 'profile' });

      picker.distanceKm.set(500);
      fixture.detectChanges();

      expect(picker.radiusValid()).toBe(false);
      expect(errorText()).toBe('WHERE.RADIUS_RANGE');
    });

    it('refuses zero and an empty field', () => {
      const picker = create({ distanceKm: 1, mode: 'profile' });

      picker.distanceKm.set(0);
      fixture.detectChanges();
      expect(picker.radiusValid()).toBe(false);

      picker.distanceKm.set(null);
      fixture.detectChanges();
      expect(picker.radiusValid()).toBe(false);
    });

    it('accepts a radius inside the range, from a place too', () => {
      const picker = create({ distanceKm: 100, mode: 'place', placeLabel: 'work' });

      expect(picker.radiusValid()).toBe(true);
      expect(errorText()).toBe('');
    });

    it('leaves a stored radius past 100 km alone until it is changed', () => {
      const picker = create({ distanceKm: 500, mode: 'profile' }, { storedRadiusKm: 500 });

      expect(picker.radiusValid()).toBe(true);
      expect(errorText()).toBe('');

      picker.distanceKm.set(600);
      fixture.detectChanges();
      expect(picker.radiusValid()).toBe(false);
    });

    it('ignores the radius field when the alarm is not measured by one', () => {
      const picker = create({ distanceKm: 500, mode: 'profile' });

      picker.mode.set('areas');
      fixture.detectChanges();
      expect(picker.radiusValid()).toBe(true);

      picker.mode.set('inherit');
      fixture.detectChanges();
      expect(picker.radiusValid()).toBe(true);
    });
  });

  /**
   * Only the card's sheet passed the profile's areas, so every dialog said "Anywhere I get alerts" under
   * "Anywhere in my areas" even for a profile with three. The picker already reads them for its area
   * list, so it summarises them itself.
   */
  describe('the inherited option', () => {
    function detail(): string {
      return (fixture.nativeElement as HTMLElement).querySelector('.scope-option-detail')?.textContent?.trim() ?? '';
    }

    it('names the profile areas it read itself, title-cased', () => {
      create({ mode: 'profile' });
      const http = TestBed.inject(HttpTestingController);

      http.expectOne('http://test/api/areas/available').flush([]);
      http.expectOne('http://test/api/areas').flush(['downtown orlando', 'kissimmee']);
      http.match(() => true).forEach(r => r.flush([]));
      fixture.detectChanges();

      expect(detail()).toBe('Downtown Orlando, Kissimmee');
    });

    it('still names them when the instance area list fails to load', () => {
      create({ mode: 'profile' });
      const http = TestBed.inject(HttpTestingController);

      http.expectOne('http://test/api/areas/available').flush('boom', { status: 500, statusText: 'Server Error' });
      http.expectOne('http://test/api/areas').flush(['kissimmee']);
      http.match(() => true).forEach(r => r.flush([]));
      fixture.detectChanges();

      expect(detail()).toBe('Kissimmee');
    });

    it('prefers the areas the host passed', () => {
      create({ mode: 'profile' }, { profileAreas: ['avoca beach'] });

      expect(detail()).toBe('Avoca Beach');
    });

    it('falls back to "anywhere" for a profile with no areas', () => {
      create({ mode: 'profile' });
      const http = TestBed.inject(HttpTestingController);

      http.match(() => true).forEach(r => r.flush([]));
      fixture.detectChanges();

      expect(detail()).toBe('WHERE.PROFILE_ANYWHERE');
    });
  });

  /** A small stored radius is shown and kept as it is; the 0.1 km floor is for a radius someone types. */
  describe('a radius under 0.1 km', () => {
    it('opens a 40 m pin rule on "near a point" with its radius, and lets it stand', () => {
      const picker = create({ distanceKm: 0.04, mode: 'profile' }, { storedRadiusKm: 0.04 });

      expect(picker.mode()).toBe('near');
      expect(picker.distanceKm()).toBe(0.04);
      expect(picker.radiusValid()).toBe(true);
      expect(picker.scope()).toEqual({ distanceKm: 0.04, mode: 'profile' });
    });

    it('keeps a 30 m place radius instead of seeding 1 km', () => {
      const picker = create({ distanceKm: 0.03, mode: 'place', placeLabel: 'work' }, { storedRadiusKm: 0.03 });

      expect(picker.scope()).toEqual({ distanceKm: 0.03, mode: 'place', placeLabel: 'work' });
    });

    it('refuses the same small radius when the user types it', () => {
      const picker = create({ distanceKm: 2, mode: 'profile' }, { storedRadiusKm: 2 });

      picker.distanceKm.set(0.05);
      fixture.detectChanges();

      expect(picker.radiusValid()).toBe(false);
    });
  });

  it('reopens an emptied radius as "near a point" still refused, not as a different choice', () => {
    // A tab switch destroys and rebuilds the picker from the scope it last emitted.
    const picker = create({ distanceKm: 0, mode: 'profile' });

    expect(picker.mode()).toBe('near');
    expect(picker.radiusValid()).toBe(false);
  });

  it("summarises the profile's areas with a drawn fence's own name, not its stored key", () => {
    const picker = create({ mode: 'profile' }, { profileAreas: ['lake eola loop 2', 'terrigal'] });
    const http = TestBed.inject(HttpTestingController);

    http
      .match('http://test/api/geofences/custom')
      .forEach(r => r.flush([{ id: 1, displayName: 'Lake Eola Loop', kojiName: 'lake eola loop 2' }]));
    http.match(() => true).forEach(r => r.flush([]));
    fixture.detectChanges();

    expect(picker.profileAreaSummary()).toBe('Lake Eola Loop, Terrigal');
  });
});
