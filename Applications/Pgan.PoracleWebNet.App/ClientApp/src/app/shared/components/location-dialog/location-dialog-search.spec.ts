import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LocationDialogComponent } from './location-dialog.component';
import { GeocodingResult } from '../../../core/models';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';

/**
 * The address search input is bound with `[(ngModel)]`, and picking an autocomplete option writes the
 * option's value -- a GeocodingResult object -- through that binding. It reached the search stream,
 * whose `q.trim()` threw, and the stream died: every search after the first pick returned nothing.
 */
describe('LocationDialogComponent address search', () => {
  let component: LocationDialogComponent;
  let geocode: jest.Mock;

  const result: GeocodingResult = { display_name: '1 Main St, Richmond, VA', lat: '37.54', lon: '-77.43' } as GeocodingResult;

  beforeEach(() => {
    jest.useFakeTimers();
    geocode = jest.fn(() => of([result]));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: null },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: LocationService, useValue: { geocode, reverseGeocode: () => of(null) } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
      ],
    });
    // Constructed without a render, so Leaflet never initialises a map in jsdom.
    component = TestBed.createComponent(LocationDialogComponent).componentInstance;
    component.ngOnInit();
  });

  afterEach(() => {
    component.ngOnDestroy();
    jest.useRealTimers();
  });

  it('searches for what the user types', () => {
    component.onSearchChange('main st');
    jest.advanceTimersByTime(500);

    expect(geocode).toHaveBeenCalledWith('main st');
    expect(component.searchResults()).toEqual([result]);
  });

  it('still searches after an address has been picked', () => {
    component.onSearchChange('main st');
    jest.advanceTimersByTime(500);

    // What ngModel emits when an option is chosen, followed by the option's own handler.
    component.onSearchChange(result as unknown as string);
    component.selectResult(result);
    jest.advanceTimersByTime(500);

    component.onSearchChange('broad st');
    jest.advanceTimersByTime(500);

    expect(geocode).toHaveBeenLastCalledWith('broad st');
    expect(component.searchResults()).toEqual([result]);
  });

  it('searches the same text again after a pick', () => {
    component.onSearchChange('main st');
    jest.advanceTimersByTime(500);
    component.onSearchChange(result as unknown as string);
    component.selectResult(result);
    jest.advanceTimersByTime(500);
    const before = geocode.mock.calls.length;

    component.onSearchChange('main st');
    jest.advanceTimersByTime(500);

    expect(geocode.mock.calls.length).toBeGreaterThan(before);
  });

  it('shows the picked address in the input, not [object Object]', () => {
    expect(component.displayResult(result)).toBe('1 Main St, Richmond, VA');
    expect(component.displayResult('typed text')).toBe('typed text');
    expect(component.displayResult(null)).toBe('');
  });
});
