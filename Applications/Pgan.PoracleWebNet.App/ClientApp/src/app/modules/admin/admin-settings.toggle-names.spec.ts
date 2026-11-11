import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent, SETTING_GROUPS } from './admin-settings.component';
import { SettingsService } from '../../core/services/settings.service';

/**
 * Every group header is a toggle button. They all used to be named "Collapse section", so a screen reader
 * listed nine identical buttons. The name carries the group; `aria-expanded` carries the state.
 */
describe('AdminSettingsComponent section toggle names', () => {
  const create = () => {
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
            isForcedByPoracle: () => false,
            siteSettings: () => ({}),
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });
    const translate = TestBed.inject(TranslateService);
    const translations: Record<string, string> = { SECTION_TOGGLE: '{{section}} section' };
    for (const g of SETTING_GROUPS) {
      translations[g.labelKey.replace('ADMIN_SETTINGS.', '')] = `Label of ${g.labelKey}`;
    }
    translate.setTranslation('en', { ADMIN_SETTINGS: translations });
    translate.use('en');
    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('gives every group toggle a distinct accessible name that includes its group', () => {
    const toggles = Array.from(create().querySelectorAll<HTMLElement>('.section-header-toggle'));
    const names = toggles.map(t => t.getAttribute('aria-label') ?? '');

    expect(toggles.length).toBeGreaterThan(1);
    expect(new Set(names).size).toBe(names.length);
    for (const t of toggles) {
      const label = t.querySelector('.section-label')!.textContent!.trim();
      expect(t.getAttribute('aria-label')).toContain(label);
    }
  });

  it('still states the open/closed state through aria-expanded', () => {
    const toggles = Array.from(create().querySelectorAll<HTMLElement>('.section-header-toggle'));

    for (const t of toggles) {
      expect(['true', 'false']).toContain(t.getAttribute('aria-expanded'));
    }
  });
});
