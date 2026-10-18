import { Component, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { displayName, PersonalFilterPanelComponent } from './personal-filter-panel.component';
import { PlayerAccount, PoracleServerConfig } from '../../../core/models';
import { AuthService } from '../../../core/services/auth.service';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';
import { GameAccountsDialogComponent } from '../game-accounts-dialog/game-accounts-dialog.component';

@Component({
  imports: [PersonalFilterPanelComponent],
  standalone: true,
  template: '<app-personal-filter-panel [shiny]="shiny" [background]="background" />',
})
class HostComponent {
  background = new FormControl<boolean | null>(false);
  shiny = new FormControl<boolean | null>(false);
}

describe('PersonalFilterPanelComponent', () => {
  let accounts: WritableSignal<PlayerAccount[] | null>;
  let serverConfig: WritableSignal<Partial<PoracleServerConfig>>;
  let supportUrl: WritableSignal<string | null>;
  let load: jest.Mock;
  let dialog: { open: jest.Mock };

  const account = (playerId: string, label: string | null = null): PlayerAccount => ({
    createdAt: '2026-10-01T00:00:00Z',
    label,
    playerId,
  });

  function create(
    opts: { admin?: boolean; background?: boolean; feed?: boolean; impersonating?: boolean; selfService?: boolean; shiny?: boolean } = {},
  ): ComponentFixture<HostComponent> {
    serverConfig = signal({ personalRatesConfigured: opts.feed ?? true, playerAccountsSelfService: opts.selfService ?? false });
    load = jest.fn(() => of(accounts()));
    dialog = { open: jest.fn(() => ({ afterClosed: () => of(undefined) })) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: PlayerAccountService, useValue: { accounts, load } },
        { provide: PoracleConfigService, useValue: { serverConfig } },
        { provide: AuthService, useValue: { isAdmin: () => opts.admin ?? false, isImpersonating: () => opts.impersonating ?? false } },
        { provide: SettingsService, useValue: { supportUrl } },
        { provide: MatDialog, useValue: dialog },
      ],
      imports: [HostComponent],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.shiny.setValue(opts.shiny ?? false);
    fixture.componentInstance.background.setValue(opts.background ?? false);
    fixture.detectChanges();
    return fixture;
  }

  const text = (f: ComponentFixture<HostComponent>): string => (f.nativeElement as HTMLElement).textContent ?? '';
  const status = (f: ComponentFixture<HostComponent>): string =>
    ((f.nativeElement as HTMLElement).querySelector('.panel-status')?.textContent ?? '').trim();

  beforeEach(() => {
    accounts = signal<PlayerAccount[] | null>([]);
    supportUrl = signal<string | null>(null);
  });

  it('says nothing about accounts while both toggles are off', () => {
    const f = create({ shiny: false });

    expect(text(f)).toContain('POKEMON.PERSONAL_SHINY');
    expect(text(f)).toContain('POKEMON.PERSONAL_BACKGROUND');
    expect(status(f)).toBe('');
  });

  it('reads the accounts when it opens', () => {
    create();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('counts a value the dialog seeded from a stored rule', () => {
    // An edit of a stored shiny rule opens with the toggle already on; the status must follow it untouched.
    const f = create({ selfService: true, shiny: true });

    expect(status(f)).toContain('POKEMON.PERSONAL_NO_ACCOUNTS');
  });

  it('says the server has no rate feed before anything about accounts', () => {
    accounts.set([account('AbC123', 'Main')]);
    const f = create({ feed: false, shiny: true });

    expect(status(f)).toContain('POKEMON.PERSONAL_NO_FEED');
    expect(status(f)).not.toContain('POKEMON.PERSONAL_CHECKING');
  });

  it('offers to link an account when the trainer may', () => {
    const f = create({ selfService: true, shiny: true });

    expect(status(f)).toContain('POKEMON.PERSONAL_NO_ACCOUNTS');
    const button = (f.nativeElement as HTMLElement).querySelector('.link-button') as HTMLButtonElement;
    expect(button).not.toBeNull();

    button.click();
    expect(dialog.open).toHaveBeenCalledWith(GameAccountsDialogComponent);
    // Re-read on close, so linking an account updates this line without reopening the rule.
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('offers to link an account to an administrator even with self-service off', () => {
    const f = create({ admin: true, selfService: false, shiny: true });

    expect((f.nativeElement as HTMLElement).querySelector('.link-button')).not.toBeNull();
  });

  it('points a trainer who cannot link at an admin, with the support link when one is set', () => {
    supportUrl.set('https://support.example.com');
    const f = create({ background: true, selfService: false });

    expect(status(f)).toContain('POKEMON.PERSONAL_ASK_ADMIN');
    expect((f.nativeElement as HTMLElement).querySelector('.link-button')).toBeNull();
    const link = (f.nativeElement as HTMLElement).querySelector('.support-link') as HTMLAnchorElement;
    expect(link.href).toBe('https://support.example.com/');
  });

  it('never offers the link while impersonating, even to an administrator', () => {
    const f = create({ admin: true, impersonating: true, selfService: true, shiny: true });

    expect(status(f)).toContain('POKEMON.PERSONAL_ASK_ADMIN');
    expect((f.nativeElement as HTMLElement).querySelector('.link-button')).toBeNull();
  });

  it('says nothing when the accounts could not be read', () => {
    // null is "could not read", not "none linked": claiming the trainer's link vanished would be false.
    accounts.set(null);
    const f = create({ selfService: true, shiny: true });

    expect(status(f)).toBe('');
  });

  it('names the linked accounts it will check', () => {
    accounts.set([account('AbC123', 'Main')]);
    let f = create({ shiny: true });
    expect(status(f)).toContain('POKEMON.PERSONAL_CHECKING_ONE');

    accounts.set([account('AbC123', 'Main'), account('Zed9')]);
    f = create({ shiny: true });
    expect(status(f)).toContain('POKEMON.PERSONAL_CHECKING_TWO');

    accounts.set([account('AbC123', 'Main'), account('Zed9'), account('Qq77')]);
    f = create({ shiny: true });
    expect(status(f)).toContain('POKEMON.PERSONAL_CHECKING_MANY');
  });

  it('follows a toggle switched on after opening', () => {
    const f = create();
    expect(status(f)).toBe('');

    f.componentInstance.background.setValue(true);
    f.detectChanges();

    expect(status(f)).toContain('POKEMON.PERSONAL_ASK_ADMIN');
  });

  it('explains what both toggles together mean', () => {
    const f = create({ background: true, shiny: true });

    expect(text(f)).toContain('POKEMON.PERSONAL_BOTH');
    expect(text(create({ shiny: true }))).not.toContain('POKEMON.PERSONAL_BOTH');
  });

  describe('displayName', () => {
    it('uses the label', () => {
      expect(displayName(account('AbC123', 'Main'))).toBe('Main');
    });

    it('falls back to the end of the player id, as PoracleNG names an unlabelled account', () => {
      expect(displayName(account('AbC123'))).toBe('…C123');
      expect(displayName(account('AbC123', '   '))).toBe('…C123');
    });
  });
});
