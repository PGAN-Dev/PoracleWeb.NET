import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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
});
