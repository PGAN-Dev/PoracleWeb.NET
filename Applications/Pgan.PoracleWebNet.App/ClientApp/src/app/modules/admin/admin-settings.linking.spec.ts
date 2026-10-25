import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AdminSettingsComponent, SETTING_GROUPS, telegramRegistrationUrlProblem } from './admin-settings.component';
import { AuthProviders } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';

const BOTH_APPS: AuthProviders = {
  discord: { configured: true, enabledByAdmin: true },
  telegram: { botUsername: 'old_town_bot', configured: true, enabledByAdmin: true },
};

const LINKING_KEYS = ['disable_account_linking', 'link_register_missing', 'telegram_registration_url'];

describe('linked accounts settings group', () => {
  const group = SETTING_GROUPS.find(g => g.labelKey === 'ADMIN_SETTINGS.GROUP_LINKED_ACCOUNTS');

  it('holds the three linking settings together, and Features holds none of them', () => {
    expect(group?.settings.map(s => s.key)).toEqual(LINKING_KEYS);
    const features = SETTING_GROUPS.find(g => g.labelKey === 'ADMIN_SETTINGS.GROUP_FEATURES')!;
    expect(features.settings.filter(s => LINKING_KEYS.includes(s.key))).toEqual([]);
  });

  it.each(['disable_account_linking', 'link_register_missing'])('keeps %s a boolean with a label and description', key => {
    const setting = group?.settings.find(s => s.key === key);
    expect(setting?.type).toBe('boolean');
    expect(setting?.labelKey).toBeTruthy();
    expect(setting?.descriptionKey).toBeTruthy();
  });
});

/** Mirrors the server's rule: empty clears it, otherwise an https link on t.me or telegram.me. */
describe('telegramRegistrationUrlProblem', () => {
  it.each(['', '   ', 'https://t.me/+abc123', 'https://telegram.me/joinchat/xyz', 'https://T.ME/old_town', ' https://t.me/x '])(
    'accepts %p',
    value => expect(telegramRegistrationUrlProblem(value)).toBeNull(),
  );

  it.each(['http://t.me/x', 'https://example.com/t.me', 'https://t.me.evil.example/x', 't.me/+abc', 'not a link', 'javascript:alert(1)'])(
    'refuses %p with the human message',
    value => expect(telegramRegistrationUrlProblem(value)).toBe('ADMIN_SETTINGS.TELEGRAM_REGISTRATION_URL_INVALID'),
  );
});

describe('AdminSettingsComponent linked accounts settings', () => {
  const create = (values: Record<string, string>, providers: AuthProviders | null = BOTH_APPS) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: AuthService,
          useValue: { getProviders: () => (providers ? of(providers) : throwError(() => new Error('offline'))) },
        },
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of(Object.entries(values).map(([key, value]) => ({ key, value }))),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => values,
            update: jest.fn(() => of({})),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture;
  };
  const row = (el: HTMLElement, key: string) => el.querySelector(`[data-test="setting-${key}"]`)!;
  const urlInput = (el: HTMLElement) => row(el, 'telegram_registration_url').querySelector('input') as HTMLInputElement;
  const type = (fixture: ReturnType<typeof create>, value: string) => {
    const input = urlInput(fixture.nativeElement);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const saveButton = (el: HTMLElement) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('.settings-actionbar button')).find(b =>
      b.textContent?.includes('ADMIN_SETTINGS.SAVE_CHANGES'),
    )!;

  it('suggests the shape of an invite link', () => {
    const fixture = create({ telegram_registration_url: '' });
    expect(urlInput(fixture.nativeElement).getAttribute('placeholder')).toBe('https://t.me/+…');
  });

  it('marks a link that is not on t.me invalid, says why in words, and holds Save', () => {
    const fixture = create({ telegram_registration_url: '' });
    type(fixture, 'https://example.com/join');
    const el = fixture.nativeElement as HTMLElement;
    expect(urlInput(el).getAttribute('aria-invalid')).toBe('true');
    const error = row(el, 'telegram_registration_url').querySelector('mat-error');
    expect(error?.textContent).toContain('ADMIN_SETTINGS.TELEGRAM_REGISTRATION_URL_INVALID');
    expect(error?.textContent).not.toContain('telegram_registration_url');
    expect(saveButton(el).disabled).toBe(true);
  });

  it('leaves a valid invite link, and an emptied field, unmarked and saveable', () => {
    const fixture = create({ telegram_registration_url: 'https://t.me/+old' });
    type(fixture, 'https://t.me/+abc123');
    const el = fixture.nativeElement as HTMLElement;
    expect(urlInput(el).getAttribute('aria-invalid')).toBe('false');
    expect(row(el, 'telegram_registration_url').querySelector('mat-error')).toBeNull();
    expect(saveButton(el).disabled).toBe(false);
    type(fixture, '');
    expect(saveButton(el).disabled).toBe(false);
  });

  it('dims the settings that depend on linking while linking is off, and says how to change them', async () => {
    const fixture = create({ disable_account_linking: 'True', link_register_missing: 'False', telegram_registration_url: '' });
    // ngModel applies `disabled` a tick later.
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(urlInput(el).disabled).toBe(true);
    expect(row(el, 'link_register_missing').querySelector('mat-slide-toggle button')?.hasAttribute('disabled')).toBe(true);
    expect(row(el, 'link_register_missing').textContent).toContain('ADMIN_SETTINGS.LINKING_OFF_HINT');
    // The switch that turns linking back on is never the one dimmed.
    expect(row(el, 'disable_account_linking').querySelector('mat-slide-toggle button')?.hasAttribute('disabled')).toBe(false);
  });

  it('leaves them enabled while linking is on', async () => {
    const fixture = create({ disable_account_linking: 'False', link_register_missing: 'False', telegram_registration_url: '' });
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(urlInput(el).disabled).toBe(false);
    expect(row(el, 'link_register_missing').querySelector('mat-slide-toggle button')?.hasAttribute('disabled')).toBe(false);
    expect(row(el, 'link_register_missing').textContent).not.toContain('ADMIN_SETTINGS.LINKING_OFF_HINT');
  });

  describe('when one app cannot be linked here', () => {
    const notes = (el: HTMLElement) =>
      Array.from(el.querySelectorAll('[data-test="linking-app-gap"]')).map(n => n.querySelector('span')?.textContent?.trim());

    it('shows no note when both apps can sign in', () => {
      const fixture = create({ disable_account_linking: 'False' });
      expect(notes(fixture.nativeElement)).toEqual([]);
    });

    it('names an app that is not set up in .env', () => {
      const fixture = create({ disable_account_linking: 'False' }, { ...BOTH_APPS, discord: { configured: false, enabledByAdmin: true } });
      expect(notes(fixture.nativeElement)).toEqual(['ADMIN_SETTINGS.LINKING_APP_NOT_SET_UP']);
    });

    it('names an app whose sign-in switch is off', () => {
      const fixture = create({ disable_account_linking: 'False', enable_telegram: 'false' });
      expect(notes(fixture.nativeElement)).toEqual(['ADMIN_SETTINGS.LINKING_APP_SWITCHED_OFF']);
    });

    it('puts the note in the Linked accounts group', () => {
      const fixture = create({ disable_account_linking: 'False', enable_telegram: 'false' });
      const note = (fixture.nativeElement as HTMLElement).querySelector('[data-test="linking-app-gap"]')!;
      expect(note.closest('.settings-section')?.textContent).toContain('ADMIN_SETTINGS.DISABLE_ACCOUNT_LINKING_LABEL');
    });

    it('says nothing while linking is off, nor when the providers could not be read', () => {
      expect(notes(create({ disable_account_linking: 'True', enable_telegram: 'false' }).nativeElement)).toEqual([]);
      expect(notes(create({ disable_account_linking: 'False', enable_telegram: 'false' }, null).nativeElement)).toEqual([]);
    });
  });
});
