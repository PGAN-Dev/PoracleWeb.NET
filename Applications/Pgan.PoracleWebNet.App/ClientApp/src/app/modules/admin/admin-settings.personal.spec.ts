import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { PoracleServerConfig } from '../../core/models';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { SettingsService } from '../../core/services/settings.service';

/**
 * Personal shiny and background rules match against a rate feed. With none configured in PoracleNG
 * every one of them matches nothing, and nothing in the rule list says so, so the administrator who can
 * fix it is told here.
 */
describe('AdminSettingsComponent personal rate feed warning', () => {
  const render = (config: Partial<PoracleServerConfig>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: PoracleConfigService, useValue: { load: () => of(config), serverConfig: signal(config) } },
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

    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const warning = (page: HTMLElement): HTMLElement | null => page.querySelector('.personal-warning');

  it('warns when personal is supported and no rate feed is configured', () => {
    const page = render({ personalRatesConfigured: false, personalSupported: true });

    expect(warning(page)?.textContent).toContain('ADMIN.PERSONAL_NO_RATE_FEED');
  });

  it('says nothing once a rate feed is configured', () => {
    expect(warning(render({ personalRatesConfigured: true, personalSupported: true }))).toBeNull();
  });

  it('says nothing on a PoracleNG without personal alerts, where "no feed" is not a problem', () => {
    expect(warning(render({ personalRatesConfigured: false, personalSupported: false }))).toBeNull();
  });
});
