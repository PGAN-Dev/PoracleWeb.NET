import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { SettingsService } from '../../core/services/settings.service';
import { ALARM_TYPE_STYLE } from '../../shared/utils/alarm-type-style';

/**
 * The page's shape: search first, the versions and rule repair as one status line rather than two cards
 * ahead of every setting, and colour only where it means something.
 */
describe('AdminSettingsComponent layout', () => {
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
    return fixture.nativeElement as HTMLElement;
  };

  it('puts the search ahead of the status line, and the status line ahead of every group', () => {
    const el = create({ custom_title: 'Site' });
    const search = el.querySelector('.settings-search')!;
    const strip = el.querySelector('app-settings-status-strip')!;
    const firstGroup = el.querySelector('.settings-section')!;

    expect(search.compareDocumentPosition(strip) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(strip.compareDocumentPosition(firstGroup) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('gives group headers no colour of their own', () => {
    const el = create({ custom_title: 'Site' });
    const headers = Array.from(el.querySelectorAll<HTMLElement>('.section-header'));

    expect(headers.length).toBeGreaterThan(0);
    for (const header of headers) {
      expect(header.getAttribute('style') ?? '').toBe('');
      expect(header.querySelector('.section-icon')?.getAttribute('style') ?? '').toBe('');
    }
  });

  it('puts each alarm type’s nav icon and colour on its switch, and on no other row', () => {
    const el = create({ disable_raids: 'True', custom_title: 'Site', disable_mons: 'False' });
    const icons = Array.from(el.querySelectorAll<HTMLElement>('[data-test="alarm-type-icon"]'));

    expect(icons.slice(0, 2).map(i => i.textContent?.trim())).toEqual([
      ALARM_TYPE_STYLE['disable_mons'].icon,
      ALARM_TYPE_STYLE['disable_raids'].icon,
    ]);
    expect(icons).toHaveLength(Object.keys(ALARM_TYPE_STYLE).length);
    expect(icons[0].style.color).toBe('rgb(76, 175, 80)');
    // The branding row beside them carries none.
    const titleRow = Array.from(el.querySelectorAll('.setting-row')).find(r => r.textContent?.includes('CUSTOM_TITLE_LABEL'));
    expect(titleRow?.querySelector('[data-test="alarm-type-icon"]')).toBeNull();
  });
});
