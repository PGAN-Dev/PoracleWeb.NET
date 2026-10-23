import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { LinkedAccountsComponent } from './linked-accounts.component';
import { AuthProviders, LinkStatus } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';
import { SettingsService } from '../../core/services/settings.service';

const providers = (discord: boolean, telegram: boolean): AuthProviders => ({
  discord: { configured: discord, enabledByAdmin: true },
  telegram: { botUsername: telegram ? 'old_town_bot' : '', configured: telegram, enabledByAdmin: true },
});

describe('LinkedAccountsComponent', () => {
  const status = signal<LinkStatus | null>({ linked: false });
  const linkedAccounts = {
    linkTelegram: jest.fn(() => Promise.resolve()),
    refresh: jest.fn(() => Promise.resolve()),
    setDefault: jest.fn(() => Promise.resolve()),
    startDiscordLink: jest.fn(() => Promise.resolve()),
    status,
    unlink: jest.fn(() => Promise.resolve()),
  };
  const snackBar = { open: jest.fn() };

  function setup(options: { discord?: boolean; support?: string | null; telegram?: boolean; type?: string } = {}) {
    const auth = {
      getProviders: jest.fn(() => of(providers(options.discord ?? true, options.telegram ?? true))),
      getTelegramConfig: jest.fn(() => of({ botUsername: 'old_town_bot', enabled: options.telegram ?? true })),
      user: signal({ id: '100000000000000001', type: options.type ?? 'telegram:user' }),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AuthService, useValue: auth },
        { provide: LinkedAccountService, useValue: linkedAccounts },
        { provide: SettingsService, useValue: { supportUrl: signal(options.support ?? null) } },
      ],
      imports: [LinkedAccountsComponent, NoopAnimationsModule],
    });
    // The component imports both modules, which provide their own copies at component level.
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideProvider(MatDialog, { useValue: { open: jest.fn(() => ({ afterClosed: () => of(true) })) } });
    const fixture = TestBed.createComponent(LinkedAccountsComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, fixture };
  }

  const q = (fixture: ReturnType<typeof setup>['fixture'], sel: string) => (fixture.nativeElement as HTMLElement).querySelector(sel);

  beforeEach(() => {
    status.set({ linked: false });
    jest.clearAllMocks();
    window.history.replaceState(null, '', '/linked-accounts');
  });
  afterEach(() => TestBed.resetTestingModule());

  it('offers Link Discord to a Telegram session when Discord is configured', () => {
    const { fixture } = setup({ type: 'telegram:user' });
    expect(q(fixture, '[data-test="link-discord"]')).toBeTruthy();
    expect(q(fixture, 'app-telegram-login-button')).toBeNull();
  });

  it('hides Link Discord when Discord is not configured', () => {
    const { fixture } = setup({ discord: false, type: 'telegram:user' });
    expect(q(fixture, '[data-test="link-discord"]')).toBeNull();
  });

  it('renders the Telegram widget for a Discord session, and hides it when Telegram is not configured', () => {
    expect(q(setup({ type: 'discord:user' }).fixture, 'app-telegram-login-button')).toBeTruthy();
    TestBed.resetTestingModule();
    expect(q(setup({ telegram: false, type: 'discord:user' }).fixture, 'app-telegram-login-button')).toBeNull();
  });

  it('starts the Discord link on click', () => {
    const { fixture } = setup({ type: 'telegram:user' });
    (q(fixture, '[data-test="link-discord"]') as HTMLButtonElement).click();
    expect(linkedAccounts.startDiscordLink).toHaveBeenCalled();
  });

  it('shows the translated refusal for #link_error=link_taken and clears the fragment', () => {
    window.history.replaceState(null, '', '/linked-accounts#link_error=link_taken');
    const { fixture } = setup();
    expect(q(fixture, '[data-test="link-error"]')?.textContent).toContain('LINKED.ERR_LINK_TAKEN');
    expect(window.location.hash).toBe('');
  });

  it('reads an unknown link_error as the generic sentence, never as itself', () => {
    window.history.replaceState(null, '', '/linked-accounts#link_error=weird');
    const { fixture } = setup();
    expect(q(fixture, '[data-test="link-error"]')?.textContent).toContain('LINKED.ERR_LINK_FAILED');
  });

  it('confirms #linked=discord, refreshes and clears the fragment', () => {
    window.history.replaceState(null, '', '/linked-accounts#linked=discord');
    setup();
    expect(snackBar.open).toHaveBeenCalled();
    expect(linkedAccounts.refresh).toHaveBeenCalled();
    expect(window.location.hash).toBe('');
  });

  describe('linked', () => {
    const both: LinkStatus = {
      sides: [
        { side: 'discord', exists: true, pause: 'none' },
        { side: 'telegram', exists: true, pause: 'stopped' },
      ],
      defaultDestination: 'discord',
      linked: true,
    };

    it('shows each side and its state', () => {
      status.set(both);
      const { fixture } = setup();
      expect(q(fixture, '[data-test="state-discord"]')?.textContent).toContain('LINKED.STATE_ACTIVE');
      expect(q(fixture, '[data-test="state-telegram"]')?.textContent).toContain('LINKED.STATE_STOPPED');
    });

    it('says a side with no human is waiting to register and gives the Telegram hint', () => {
      status.set({ ...both, sides: [both.sides![0], { side: 'telegram', exists: false, pause: 'not_registered' }] });
      const { fixture } = setup();
      expect(q(fixture, '[data-test="state-telegram"]')?.textContent).toContain('LINKED.STATE_WAITING');
      expect(q(fixture, '[data-test="hint-telegram"]')).toBeTruthy();
    });

    it('names the support link in the Discord hint only when one is set', () => {
      status.set({ ...both, sides: [{ side: 'discord', exists: false, pause: 'not_registered' }, both.sides![1]] });
      expect(q(setup({ support: 'https://help.example/old-town' }).fixture, '[data-test="support-link"]')).toBeTruthy();
      TestBed.resetTestingModule();
      const { fixture } = setup();
      expect(q(fixture, '[data-test="hint-discord"]')).toBeTruthy();
      expect(q(fixture, '[data-test="support-link"]')).toBeNull();
    });

    it('changing the default radio calls setDefault', () => {
      status.set(both);
      const { fixture } = setup();
      (q(fixture, 'input[type="radio"][value="telegram"]') as HTMLInputElement).click();
      expect(linkedAccounts.setDefault).toHaveBeenCalledWith('telegram');
    });

    it('hides the default choice while one side does not exist yet', () => {
      status.set({ ...both, sides: [both.sides![0], { side: 'telegram', exists: false, pause: 'not_registered' }] });
      expect(q(setup().fixture, '[data-test="default-destination"]')).toBeNull();
    });

    it('unlinks after confirmation', async () => {
      status.set(both);
      const { fixture } = setup();
      (q(fixture, '[data-test="unlink"]') as HTMLButtonElement).click();
      await Promise.resolve();
      expect(linkedAccounts.unlink).toHaveBeenCalled();
    });
  });
});
