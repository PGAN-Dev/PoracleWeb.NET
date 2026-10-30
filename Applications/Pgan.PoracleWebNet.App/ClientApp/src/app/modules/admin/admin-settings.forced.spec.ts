import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { SettingsService } from '../../core/services/settings.service';

/**
 * A switch Poracle forces off is locked and explained. The explanation for an alarm type (Poracle drops
 * the webhooks, its bot refuses the command) is wrong for Geocoding, which PoracleNG forces off by having
 * no geocoder at all, so that row gets its own note (#1111).
 */
describe('AdminSettingsComponent explains a switch Poracle forces off', () => {
  const create = (forced: string[]) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of([]),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: (key: string) => forced.includes(key),
            siteSettings: () => ({}),
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture;
  };

  const hintFor = (forced: string[], key: string): null | string => {
    const fixture = create(forced);
    fixture.componentInstance.searchQuery.set(key);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('.forced-by-poracle-hint span')?.textContent?.trim() ?? null;
  };

  it('says PoracleNG has no geocoder when Geocoding is forced off', () => {
    expect(hintFor(['disable_nominatim'], 'disable_nominatim')).toBe('ADMIN_SETTINGS.FORCED_BY_PORACLE_GEOCODING');
  });

  it('keeps the webhook explanation for an alarm type', () => {
    expect(hintFor(['disable_nests'], 'disable_nests')).toBe('ADMIN_SETTINGS.FORCED_BY_PORACLE');
  });

  it('shows no note on Geocoding when PoracleNG has a geocoder', () => {
    expect(hintFor([], 'disable_nominatim')).toBeNull();
  });
});
