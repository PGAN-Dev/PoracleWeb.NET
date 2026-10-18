import { Component, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PersonalFilterPanelComponent } from './personal-filter-panel.component';
import { PlayerAccount, PoracleServerConfig } from '../../../core/models';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';

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
  let linkUrl: WritableSignal<string | null>;
  let load: jest.Mock;

  const account = (id: string, label: string | null = null): PlayerAccount => ({ id, createdAt: '2026-10-01T00:00:00Z', label });

  function create(
    opts: { background?: boolean; feed?: boolean; selfService?: boolean; shiny?: boolean } = {},
  ): ComponentFixture<HostComponent> {
    serverConfig = signal({ personalRatesConfigured: opts.feed ?? true, playerAccountsSelfService: opts.selfService ?? false });
    load = jest.fn(() => of(accounts()));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: PlayerAccountService, useValue: { accounts, load } },
        { provide: PoracleConfigService, useValue: { serverConfig } },
        { provide: SettingsService, useValue: { gameAccountsLinkUrl: linkUrl, supportUrl } },
      ],
      imports: [HostComponent],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.shiny.setValue(opts.shiny ?? false);
    fixture.componentInstance.background.setValue(opts.background ?? false);
    fixture.detectChanges();
    return fixture;
  }

  const el = (f: ComponentFixture<HostComponent>): HTMLElement => f.nativeElement as HTMLElement;
  const status = (f: ComponentFixture<HostComponent>): string => (el(f).querySelector('.panel-status')?.textContent ?? '').trim();

  beforeEach(() => {
    accounts = signal<PlayerAccount[] | null>([]);
    supportUrl = signal<string | null>(null);
    linkUrl = signal<string | null>(null);
  });

  it('says nothing about accounts while both toggles are off', () => {
    const f = create();

    expect(el(f).textContent).toContain('POKEMON.PERSONAL_SHINY');
    expect(el(f).textContent).toContain('POKEMON.PERSONAL_BACKGROUND');
    expect(status(f)).toBe('');
  });

  it('reads the accounts when it opens', () => {
    create();

    expect(load).toHaveBeenCalledTimes(1);
  });

  it('counts a value the dialog seeded from a stored rule', () => {
    // An edit of a stored shiny rule opens with the toggle already on; the status must follow it untouched.
    expect(status(create({ shiny: true }))).toContain('POKEMON.PERSONAL_ASK_ADMIN');
  });

  it('says the server has no rate feed before anything about accounts', () => {
    accounts.set([account('h1', 'Main')]);
    const f = create({ feed: false, shiny: true });

    expect(status(f)).toContain('POKEMON.PERSONAL_NO_FEED');
    expect(status(f)).not.toContain('POKEMON.PERSONAL_CHECKING');
  });

  describe('with no account linked, it says where linking happens, never here', () => {
    it('sends them to the map the operator configured, whatever self-service says', () => {
      linkUrl.set('https://map.example.com/profile');

      for (const selfService of [false, true]) {
        const f = create({ selfService, shiny: true });
        expect(status(f)).toContain('POKEMON.PERSONAL_LINK_ON_MAP');
        const link = el(f).querySelector('a.link-button') as HTMLAnchorElement;
        expect(link.href).toBe('https://map.example.com/profile');
        expect(link.target).toBe('_blank');
      }
    });

    it('sends them to the bot without a map, when self-service lets them use it', () => {
      const f = create({ selfService: true, shiny: true });

      expect(status(f)).toContain('POKEMON.PERSONAL_LINK_WITH_BOT');
      expect(el(f).querySelector('a.link-button')).toBeNull();
    });

    it('sends them to an admin when neither works, with the support link when one is set', () => {
      supportUrl.set('https://support.example.com');
      const f = create({ background: true });

      expect(status(f)).toContain('POKEMON.PERSONAL_ASK_ADMIN');
      expect((el(f).querySelector('.support-link') as HTMLAnchorElement).href).toBe('https://support.example.com/');
    });

    it('never offers a way to type a player id', () => {
      linkUrl.set('https://map.example.com');
      const f = create({ selfService: true, shiny: true });

      expect(el(f).querySelector('input')).toBeNull();
      expect(el(f).querySelector('button.link-button')).toBeNull();
    });
  });

  it('says nothing when the accounts could not be read', () => {
    // null is "could not read", not "none linked": claiming the trainer's link vanished would be false.
    accounts.set(null);

    expect(status(create({ selfService: true, shiny: true }))).toBe('');
  });

  it('names the linked accounts it will check', () => {
    accounts.set([account('h1', 'Main')]);
    expect(status(create({ shiny: true }))).toContain('POKEMON.PERSONAL_CHECKING_ONE');

    accounts.set([account('h1', 'Main'), account('h2')]);
    expect(status(create({ shiny: true }))).toContain('POKEMON.PERSONAL_CHECKING_TWO');

    accounts.set([account('h1', 'Main'), account('h2'), account('h3')]);
    expect(status(create({ shiny: true }))).toContain('POKEMON.PERSONAL_CHECKING_MANY');
  });

  it('names an account by its label, or by its place in the list, never by an id', () => {
    accounts.set([account('opaque-handle-one', '  Main  '), account('opaque-handle-two', null)]);
    const f = create({ shiny: true });
    const panel = f.debugElement.children[0].componentInstance as PersonalFilterPanelComponent;

    expect(panel.accountNames()).toEqual([
      { label: 'Main', n: 1 },
      { label: null, n: 2 },
    ]);
    expect(el(f).innerHTML).not.toContain('opaque-handle');
  });

  it('follows a toggle switched on after opening', () => {
    const f = create();
    expect(status(f)).toBe('');

    f.componentInstance.background.setValue(true);
    f.detectChanges();

    expect(status(f)).toContain('POKEMON.PERSONAL_ASK_ADMIN');
  });

  it('explains what both toggles together mean', () => {
    expect(el(create({ background: true, shiny: true })).textContent).toContain('POKEMON.PERSONAL_BOTH');
    expect(el(create({ shiny: true })).textContent).not.toContain('POKEMON.PERSONAL_BOTH');
  });
});
