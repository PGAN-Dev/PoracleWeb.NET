import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { FeatureReadonlyBannerComponent } from './feature-readonly-banner.component';
import { SettingsService } from '../../../core/services/settings.service';

describe('FeatureReadonlyBannerComponent', () => {
  let fixture: ComponentFixture<FeatureReadonlyBannerComponent>;

  const setup = (settings: Record<string, string>, upstream: string[] = []) => {
    const siteSettings = signal(settings);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            isDisabled: (key: string) => siteSettings()[key]?.toLowerCase() === 'true' || upstream.includes(key),
            isForcedByPoracle: (key: string) => upstream.includes(key),
            siteSettings,
          },
        },
      ],
      imports: [FeatureReadonlyBannerComponent],
    });
    fixture = TestBed.createComponent(FeatureReadonlyBannerComponent);
    fixture.componentRef.setInput('disableKey', 'disable_lures');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('renders nothing while the type is enabled', () => {
    const el = setup({});

    expect(el.querySelector('.readonly-banner')).toBeNull();
  });

  it('explains an admin-disabled type', () => {
    const el = setup({ disable_lures: 'true' });

    expect(el.querySelector('.readonly-banner')).not.toBeNull();
    expect(el.textContent).toContain('ALARM.READ_ONLY_ADMIN');
  });

  /**
   * The distinction matters: nobody can undo Poracle's setting from this side, so telling the user an
   * administrator of this site disabled it would send them to the wrong person.
   */
  it('names Poracle when the type is disabled upstream', () => {
    const el = setup({}, ['disable_lures']);

    expect(el.textContent).toContain('ALARM.READ_ONLY_PORACLE');
    expect(el.textContent).not.toContain('ALARM.READ_ONLY_ADMIN');
  });

  it('prefers the Poracle wording when both sources disable it', () => {
    const el = setup({ disable_lures: 'true' }, ['disable_lures']);

    expect(el.textContent).toContain('ALARM.READ_ONLY_PORACLE');
  });

  it('reads the key it was given, not a fixed one', () => {
    const el = setup({ disable_gyms: 'true' });

    expect(el.querySelector('.readonly-banner')).toBeNull();
  });
});
