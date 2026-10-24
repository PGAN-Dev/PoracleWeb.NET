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
  const canRead = signal(true);
  const linkedAccounts = {
    canChoose: signal(false),
    canRead,
    drift: signal([]),
    driftUnknown: signal(false),
    linkTelegram: jest.fn(() => Promise.resolve()),
    refresh: jest.fn(() => Promise.resolve()),
    setDefault: jest.fn(() => Promise.resolve()),
    startDiscordLink: jest.fn(() => Promise.resolve()),
    status,
    unlink: jest.fn(() => Promise.resolve()),
  };
  const snackBar = { open: jest.fn() };

  function setup(
    options: {
      discord?: boolean;
      impersonating?: boolean;
      support?: string | null;
      telegram?: boolean;
      telegramGroup?: string | null;
      type?: string;
    } = {},
  ) {
    const auth = {
      getProviders: jest.fn(() => of(providers(options.discord ?? true, options.telegram ?? true))),
      isImpersonating: signal(options.impersonating ?? false),
      user: signal({ id: '100000000000000001', type: options.type ?? 'telegram:user' }),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AuthService, useValue: auth },
        { provide: LinkedAccountService, useValue: linkedAccounts },
        {
          provide: SettingsService,
          useValue: { supportUrl: signal(options.support ?? null), telegramRegistrationUrl: signal(options.telegramGroup ?? null) },
        },
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
    canRead.set(true);
    jest.clearAllMocks();
    window.history.replaceState(null, '', '/linked-accounts');
  });
  afterEach(() => TestBed.resetTestingModule());

  describe('returning from Telegram', () => {
    const ret = '?id=200000001&first_name=A&auth_date=1700000000&hash=abc';
    const flush = () => new Promise(resolve => setTimeout(resolve));

    it('links from the return and clears the query', async () => {
      window.history.replaceState(null, '', '/linked-accounts' + ret);
      setup({ type: 'discord:user' });
      await flush();
      expect(linkedAccounts.linkTelegram).toHaveBeenCalledWith({ first_name: 'A', id: '200000001', auth_date: '1700000000', hash: 'abc' });
      expect(window.location.search).toBe('');
      expect(window.location.pathname).toBe('/linked-accounts');
    });

    it('does nothing without a return', async () => {
      setup({ type: 'discord:user' });
      await flush();
      expect(linkedAccounts.linkTelegram).not.toHaveBeenCalled();
    });

    it('does not link while impersonating, but still clears the query', async () => {
      window.history.replaceState(null, '', '/linked-accounts' + ret);
      setup({ impersonating: true, type: 'discord:user' });
      await flush();
      expect(linkedAccounts.linkTelegram).not.toHaveBeenCalled();
      expect(window.location.search).toBe('');
    });
  });

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

  it('shows a load-failed state with Retry, not Link, when the status could not be read', async () => {
    status.set(null);
    const { fixture } = setup({ type: 'telegram:user' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(q(fixture, '[data-test="load-failed"]')).toBeTruthy();
    expect(q(fixture, '[data-test="link-discord"]')).toBeNull();
    (q(fixture, '[data-test="retry"]') as HTMLButtonElement).click();
    expect(linkedAccounts.refresh).toHaveBeenCalledTimes(2);
  });

  it('shows the load-failed message without Retry when a retry could not run (settings failed to load)', async () => {
    status.set(null);
    canRead.set(false);
    const { fixture } = setup({ type: 'telegram:user' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(q(fixture, '[data-test="load-failed"]')).toBeTruthy();
    expect(q(fixture, '[data-test="retry"]')).toBeNull();
  });

  it('says nothing about the viewer own link while impersonating, not an error', async () => {
    status.set(null);
    canRead.set(false);
    const { fixture } = setup({ impersonating: true, type: 'telegram:user' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(q(fixture, '[data-test="load-failed"]')).toBeNull();
    expect(q(fixture, '[data-test="retry"]')).toBeNull();
  });

  it('offers no Link while impersonating', () => {
    const { fixture } = setup({ impersonating: true, type: 'telegram:user' });
    expect(q(fixture, '[data-test="link-discord"]')).toBeNull();
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

    it('hides Unlink and the default choice while impersonating', () => {
      status.set(both);
      const { fixture } = setup({ impersonating: true });
      expect(q(fixture, '[data-test="unlink"]')).toBeNull();
      expect(q(fixture, '[data-test="default-destination"]')).toBeNull();
    });

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

    it('offers the Telegram registration group while that side waits and the setting is set', () => {
      status.set({ ...both, sides: [both.sides![0], { side: 'telegram', exists: false, pause: 'not_registered' }] });
      const { fixture } = setup({ telegramGroup: 'https://t.me/+abc123' });
      const link = q(fixture, '[data-test="telegram-group-link"]') as HTMLAnchorElement;
      expect(link.getAttribute('href')).toBe('https://t.me/+abc123');
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toContain('noopener');
    });

    it('shows no group button without the setting', () => {
      status.set({ ...both, sides: [both.sides![0], { side: 'telegram', exists: false, pause: 'not_registered' }] });
      expect(q(setup().fixture, '[data-test="telegram-group-link"]')).toBeNull();
    });

    it('shows no group button once the Telegram side is active', () => {
      status.set(both);
      expect(q(setup({ telegramGroup: 'https://t.me/+abc123' }).fixture, '[data-test="telegram-group-link"]')).toBeNull();
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

    it('shows one line about a legacy profile 0 when the server names a side', () => {
      status.set({ ...both, legacyProfileZero: 'discord' });
      const { fixture } = setup();
      expect(q(fixture, '[data-test="legacy-profile-zero"]')?.textContent).toContain('LINKED.LEGACY_PROFILE_ZERO');
    });

    it('hides the legacy profile 0 line when the server says null or says nothing', () => {
      status.set({ ...both, legacyProfileZero: null });
      expect(q(setup().fixture, '[data-test="legacy-profile-zero"]')).toBeNull();
      TestBed.resetTestingModule();
      status.set(both);
      expect(q(setup().fixture, '[data-test="legacy-profile-zero"]')).toBeNull();
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
