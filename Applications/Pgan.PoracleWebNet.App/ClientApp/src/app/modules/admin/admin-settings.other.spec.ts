import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { SettingsService } from '../../core/services/settings.service';

/**
 * The "Other" section renders every stored key the page does not otherwise know as an editable text
 * box. `hidden_areas` (#886) is written by Admin > Areas, which is the only writer that asks Poracle to
 * reload the geofence feed, so a raw box over its JSON invited the one write that does not take effect.
 * The API refuses it too; this keeps the box off the page.
 */
describe('AdminSettingsComponent "Other" section', () => {
  const create = (values: Record<string, string>) => {
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
            getAll: () => of(Object.entries(values).map(([key, value]) => ({ key, value }))),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => values,
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  const otherKeys = (sut: AdminSettingsComponent) => sut.unknownSettings().map(s => sut.itemKey(s));

  it('does not offer hidden_areas as a raw text box', () => {
    const sut = create({ hidden_areas: '["staging"]' });

    expect(otherKeys(sut)).not.toContain('hidden_areas');
  });

  it('still lists a key it has never heard of', () => {
    // The legitimate-case half: the catch-all is how an operator sees a row nothing else renders.
    const sut = create({ hidden_areas: '["staging"]', some_future_key: 'x' });

    expect(otherKeys(sut)).toEqual(['some_future_key']);
  });
});
