import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { GameAccountsDialogComponent, GameAccountsDialogData } from './game-accounts-dialog.component';
import { PlayerAccount, PoracleServerConfig } from '../../../core/models';
import { AuthService } from '../../../core/services/auth.service';
import { PlayerAccountService } from '../../../core/services/player-account.service';
import { PoracleConfigService } from '../../../core/services/poracle-config.service';
import { SettingsService } from '../../../core/services/settings.service';

describe('GameAccountsDialogComponent', () => {
  let accounts: WritableSignal<PlayerAccount[] | null>;
  let loadFailed: WritableSignal<boolean>;
  let serverConfig: WritableSignal<Partial<PoracleServerConfig>>;
  let supportUrl: WritableSignal<string | null>;
  let service: {
    accounts: unknown;
    add: jest.Mock;
    load: jest.Mock;
    loadFailed: unknown;
    relabel: jest.Mock;
    remove: jest.Mock;
    reset: jest.Mock;
  };

  const account = (playerId: string, label: string | null = null): PlayerAccount => ({
    createdAt: '2026-10-01T00:00:00Z',
    label,
    playerId,
  });

  function create(
    opts: { data?: GameAccountsDialogData; impersonating?: boolean; selfService?: boolean } = {},
  ): ComponentFixture<GameAccountsDialogComponent> {
    serverConfig = signal({ playerAccountsSelfService: opts.selfService ?? false });
    service = {
      accounts,
      add: jest.fn(() => of(account('x'))),
      load: jest.fn(() => of(accounts())),
      loadFailed,
      relabel: jest.fn(() => of(undefined)),
      remove: jest.fn(() => of(undefined)),
      reset: jest.fn(),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: opts.data ?? {} },
        { provide: PlayerAccountService, useValue: service },
        { provide: PoracleConfigService, useValue: { load: () => of(serverConfig()), serverConfig } },
        { provide: AuthService, useValue: { isImpersonating: () => opts.impersonating ?? false } },
        { provide: SettingsService, useValue: { supportUrl } },
      ],
      imports: [GameAccountsDialogComponent],
    });
    const fixture = TestBed.createComponent(GameAccountsDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  const text = (fixture: ComponentFixture<GameAccountsDialogComponent>): string => fixture.nativeElement.textContent as string;
  const query = (fixture: ComponentFixture<GameAccountsDialogComponent>, selector: string): HTMLElement | null =>
    fixture.nativeElement.querySelector(selector);

  beforeEach(() => {
    accounts = signal<PlayerAccount[] | null>([account('AbC123', 'Main'), account('Zed9')]);
    loadFailed = signal(false);
    supportUrl = signal<string | null>(null);
  });

  it('lists accounts with labels', () => {
    const fixture = create({ selfService: true });

    expect(text(fixture)).toContain('AbC123');
    expect(text(fixture)).toContain('Zed9');
    const labels = Array.from(fixture.nativeElement.querySelectorAll('.account-label') as NodeListOf<HTMLInputElement>).map(el => el.value);
    expect(labels).toEqual(['Main', '']);
  });

  it('loads the trainer own list, or the given user for an admin', () => {
    create({ selfService: true });
    expect(service.load).toHaveBeenCalledWith(undefined);

    create({ data: { displayName: 'Ash', userId: '42' } });
    expect(service.load).toHaveBeenCalledWith('42');
  });

  it('hides add and remove when self-service is off', () => {
    const fixture = create({ selfService: false });

    expect(query(fixture, '.add-form')).toBeNull();
    expect(query(fixture, '.remove-button')).toBeNull();
    expect(text(fixture)).toContain('GAME_ACCOUNTS.SELF_SERVICE_OFF');
    expect(text(fixture)).toContain('AbC123');
  });

  it('shows add and remove for an admin with self-service off', () => {
    const fixture = create({ data: { displayName: 'Ash', userId: '42' }, selfService: false });

    expect(query(fixture, '.add-form')).not.toBeNull();
    expect(query(fixture, '.remove-button')).not.toBeNull();
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.SELF_SERVICE_OFF');
  });

  it('is read-only while impersonating, even with self-service on, and does not blame self-service', () => {
    const fixture = create({ impersonating: true, selfService: true });

    expect(query(fixture, '.add-form')).toBeNull();
    expect(query(fixture, '.remove-button')).toBeNull();
    expect(text(fixture)).toContain('GAME_ACCOUNTS.IMPERSONATING');
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.SELF_SERVICE_OFF');
  });

  it("keeps a player id's case when adding", () => {
    const fixture = create({ selfService: true });
    const component = fixture.componentInstance;

    component.playerId.set('AbC_xYz9');
    component.label.set('Alt');
    component.add();

    expect(service.add).toHaveBeenCalledWith('AbC_xYz9', 'Alt', undefined);
  });

  it('does not add a blank player id', () => {
    const fixture = create({ selfService: true });
    const component = fixture.componentInstance;

    component.playerId.set('   ');
    component.add();

    expect(service.add).not.toHaveBeenCalled();
  });

  it('clears the form after a successful add and keeps it after a failed one', () => {
    const fixture = create({ selfService: true });
    const component = fixture.componentInstance;
    component.playerId.set('AbC');
    component.label.set('x');
    component.add();
    expect(component.playerId()).toBe('');
    expect(component.label()).toBe('');

    service.add.mockReturnValueOnce({ subscribe: (o: { error: () => void }) => o.error() });
    component.playerId.set('Dup');
    component.add();
    expect(component.playerId()).toBe('Dup');
  });

  it('passes the user through for an admin add and remove', () => {
    const fixture = create({ data: { displayName: 'Ash', userId: '42' } });
    const component = fixture.componentInstance;

    component.playerId.set('P');
    component.add();
    component.unlink('AbC123');

    expect(service.add).toHaveBeenCalledWith('P', '', '42');
    expect(service.remove).toHaveBeenCalledWith('AbC123', '42');
  });

  it('only relabels when the label changed', () => {
    const fixture = create({ selfService: true });
    const component = fixture.componentInstance;

    component.relabel(account('AbC123', 'Main'), 'Main');
    expect(service.relabel).not.toHaveBeenCalled();

    component.relabel(account('AbC123', 'Main'), 'Renamed');
    expect(service.relabel).toHaveBeenCalledWith('AbC123', 'Renamed', undefined);
  });

  it('says the load failed rather than showing an empty list', () => {
    accounts.set(null);
    loadFailed.set(true);
    const fixture = create({ selfService: true });

    expect(text(fixture)).toContain('GAME_ACCOUNTS.LOAD_FAILED');
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.EMPTY');
  });

  it('says there are none only once a load has returned an empty list', () => {
    accounts.set([]);
    const fixture = create({ selfService: true });

    expect(text(fixture)).toContain('GAME_ACCOUNTS.EMPTY');
  });

  it('shows the support link when self-service is off and support_url is set', () => {
    supportUrl.set('https://support.example/help');
    const fixture = create({ selfService: false });

    const link = query(fixture, 'a.support-link') as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('https://support.example/help');
  });

  it('shows no support link without a support_url, or when the trainer can edit', () => {
    expect(query(create({ selfService: false }), 'a.support-link')).toBeNull();

    supportUrl.set('https://support.example/help');
    expect(query(create({ selfService: true }), 'a.support-link')).toBeNull();
  });

  it('says whose accounts an admin is editing, and says nothing of the sort to a trainer', () => {
    // The translate stub echoes keys, so the interpolated name itself is not visible here.
    expect(text(create({ data: { displayName: 'Ash Ketchum', userId: '42' } }))).toContain('GAME_ACCOUNTS.FOR_USER');
    expect(text(create({ selfService: true }))).not.toContain('GAME_ACCOUNTS.FOR_USER');
  });

  it('forgets the list when it closes', () => {
    const fixture = create({ selfService: true });
    fixture.destroy();

    expect(service.reset).toHaveBeenCalled();
  });
});
