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

  function create(scope: AlarmScope): ScopePickerComponent {
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

    it('offers nothing from the instance when the user subscribes to nothing', () => {
      const picker = answer({ areas: [], mode: 'areas' }, ['Mechanicsville', 'Aliamanu'], []);

      expect(picker.availableAreas().filter(a => !a.own)).toEqual([]);
    });
  });
});
