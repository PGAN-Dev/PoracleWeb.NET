import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';

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
  let linkUrl: WritableSignal<string | null>;
  let snackBar: { open: jest.Mock };
  let undo: Subject<void>;
  let service: {
    accounts: unknown;
    add: jest.Mock;
    load: jest.Mock;
    loadFailed: unknown;
    relabel: jest.Mock;
    remove: jest.Mock;
    reset: jest.Mock;
    restore: jest.Mock;
  };

  /** `id` is the server's opaque handle. Nothing on the page may show it, let alone a player id. */
  const account = (id: string, label: string | null = null): PlayerAccount => ({ id, createdAt: '2026-10-01T00:00:00Z', label });

  function create(
    opts: { admin?: boolean; data?: GameAccountsDialogData; impersonating?: boolean; selfService?: boolean } = {},
  ): ComponentFixture<GameAccountsDialogComponent> {
    serverConfig = signal({ playerAccountsSelfService: opts.selfService ?? false });
    service = {
      accounts,
      add: jest.fn(() => of(account('new'))),
      load: jest.fn(() => of(accounts())),
      loadFailed,
      relabel: jest.fn(() => of(undefined)),
      remove: jest.fn(() => of('sealed-token')),
      reset: jest.fn(),
      restore: jest.fn(() => of(account('h-main', 'Main'))),
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
        { provide: AuthService, useValue: { isAdmin: () => opts.admin ?? false, isImpersonating: () => opts.impersonating ?? false } },
        { provide: SettingsService, useValue: { gameAccountsLinkUrl: linkUrl, supportUrl } },
        { provide: MatSnackBar, useValue: snackBar },
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
    accounts = signal<PlayerAccount[] | null>([account('h-main', 'Main'), account('h-2')]);
    loadFailed = signal(false);
    supportUrl = signal<string | null>(null);
    linkUrl = signal<string | null>(null);
    undo = new Subject<void>();
    snackBar = { open: jest.fn(() => ({ onAction: () => undo })) };
  });

  describe('the list', () => {
    it('names accounts by label, and an unlabelled one as "Account N" in list order', () => {
      const fixture = create();

      expect(text(fixture)).toContain('Main');
      expect(fixture.componentInstance.accountName(account('h-2'), 1)).toBe('GAME_ACCOUNTS.UNNAMED');
      expect(text(fixture)).toContain('GAME_ACCOUNTS.UNNAMED');
    });

    it('never puts an account handle on the page', () => {
      for (const opts of [{}, { selfService: true }, { admin: true, data: { userId: '42' } }]) {
        const fixture = create(opts);
        expect(text(fixture)).not.toContain('h-main');
        expect((fixture.nativeElement as HTMLElement).innerHTML).not.toContain('h-main');
      }
    });

    it('loads the trainer own list, or the given user for an admin', () => {
      create();
      expect(service.load).toHaveBeenCalledWith(undefined);

      create({ admin: true, data: { userId: '42' } });
      expect(service.load).toHaveBeenCalledWith('42');
    });

    it('says the load failed rather than showing an empty list', () => {
      accounts.set(null);
      loadFailed.set(true);
      const fixture = create();

      expect(text(fixture)).toContain('GAME_ACCOUNTS.LOAD_FAILED');
      expect(text(fixture)).not.toContain('GAME_ACCOUNTS.EMPTY');
    });

    it('says there are none only once a load has returned an empty list', () => {
      accounts.set([]);

      expect(text(create())).toContain('GAME_ACCOUNTS.EMPTY');
    });

    it('says whose accounts an admin is editing, and says nothing of the sort to a trainer', () => {
      expect(text(create({ admin: true, data: { displayName: 'Ash', userId: '42' } }))).toContain('GAME_ACCOUNTS.FOR_USER');
      expect(text(create())).not.toContain('GAME_ACCOUNTS.FOR_USER');
    });

    it('forgets the list when it closes', () => {
      const fixture = create();
      service.reset.mockClear();

      fixture.destroy();

      expect(service.reset).toHaveBeenCalled();
    });
  });

  describe("a trainer's own view", () => {
    it('never asks for a player id, whatever self-service says, even for an administrator', () => {
      for (const opts of [{}, { selfService: true }, { admin: true }]) {
        const fixture = create(opts);
        expect(query(fixture, 'input[name=playerId]')).toBeNull();
        expect(text(fixture)).not.toContain('GAME_ACCOUNTS.PLAYER_ID');
      }
    });

    it('sends them to the map when the operator has set where accounts are linked', () => {
      linkUrl.set('https://map.example.com/profile');
      const fixture = create({ selfService: true });

      expect(text(fixture)).toContain('GAME_ACCOUNTS.LINK_ON_MAP');
      const open = query(fixture, 'a.open-map') as HTMLAnchorElement;
      expect(open.href).toBe('https://map.example.com/profile');
      expect(open.target).toBe('_blank');
      expect(open.rel).toBe('noopener');
    });

    it('sends them to the bot without a map link, when self-service lets them use it', () => {
      const fixture = create({ selfService: true });

      expect(text(fixture)).toContain('GAME_ACCOUNTS.LINK_WITH_BOT');
      expect(query(fixture, 'a.open-map')).toBeNull();
    });

    it('sends them to an admin when neither works, with the support link when one is set', () => {
      supportUrl.set('https://support.example.com');
      const fixture = create({ selfService: false });

      expect(text(fixture)).toContain('GAME_ACCOUNTS.LINK_ASK_ADMIN');
      expect((query(fixture, '.support-link') as HTMLAnchorElement).href).toBe('https://support.example.com/');
    });

    it('lets them relabel and unlink with self-service on, and shows names only with it off', () => {
      let fixture = create({ selfService: true });
      expect(query(fixture, 'input.account-label')).not.toBeNull();
      expect(query(fixture, '.remove-button')).not.toBeNull();

      fixture = create({ selfService: false });
      expect(query(fixture, 'input.account-label')).toBeNull();
      expect(query(fixture, '.remove-button')).toBeNull();
      expect(query(fixture, '.account-name')?.textContent).toContain('Main');
    });

    it('is read-only while impersonating, says so, and points nowhere', () => {
      linkUrl.set('https://map.example.com');
      const fixture = create({ admin: true, impersonating: true, selfService: true });

      expect(query(fixture, '.remove-button')).toBeNull();
      expect(text(fixture)).toContain('GAME_ACCOUNTS.IMPERSONATING');
      expect(query(fixture, 'a.open-map')).toBeNull();
    });
  });

  describe('Admin > Users', () => {
    const adminView = () => create({ admin: true, data: { displayName: 'Ash', userId: '42' } });

    it('masks the player id field, with an eye to check a paste', () => {
      const fixture = adminView();
      const input = query(fixture, 'input[name=playerId]') as HTMLInputElement;
      expect(input.type).toBe('password');

      (query(fixture, '.reveal-button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(input.type).toBe('text');
      expect(query(fixture, '.reveal-button')?.getAttribute('aria-pressed')).toBe('true');
    });

    it('links by id with its case intact, clears it and masks the field again', () => {
      const fixture = adminView();
      const component = fixture.componentInstance;
      component.showPlayerId.set(true);
      component.playerId.set('AbCxYz9');
      component.label.set('Alt');

      component.add();

      expect(service.add).toHaveBeenCalledWith('AbCxYz9', 'Alt', '42');
      expect(component.playerId()).toBe('');
      expect(component.label()).toBe('');
      expect(component.showPlayerId()).toBe(false);
    });

    it('keeps what was typed after a failed link', () => {
      const component = adminView().componentInstance;
      service.add.mockReturnValueOnce({ subscribe: (o: { error: () => void }) => o.error() });
      component.playerId.set('Dup1');

      component.add();

      expect(component.playerId()).toBe('Dup1');
    });

    it('refuses anything but letters and numbers at the field, without a request', () => {
      for (const bad of ['bad id', 'AbC_123', 'a'.repeat(257)]) {
        const fixture = adminView();
        fixture.componentInstance.playerId.set(bad);
        fixture.detectChanges();
        expect(text(fixture)).toContain('GAME_ACCOUNTS.ERROR_INVALID');
        expect((query(fixture, '.add-button') as HTMLButtonElement).disabled).toBe(true);
        fixture.componentInstance.add();
        expect(service.add).not.toHaveBeenCalled();
      }
    });

    it('names a friend code for what it is, and lets twelve plain digits through with a warning', () => {
      let fixture = adminView();
      fixture.componentInstance.playerId.set('1234 5678 9012');
      fixture.detectChanges();
      expect(text(fixture)).toContain('GAME_ACCOUNTS.FRIEND_CODE_WARNING');
      expect((query(fixture, '.add-button') as HTMLButtonElement).disabled).toBe(true);

      fixture = adminView();
      fixture.componentInstance.playerId.set('123456789012');
      fixture.detectChanges();
      expect(text(fixture)).toContain('GAME_ACCOUNTS.FRIEND_CODE_WARNING');
      fixture.componentInstance.add();
      expect(service.add).toHaveBeenCalledWith('123456789012', '', '42');
    });

    it('refuses a label another account already uses, ignoring case', () => {
      const fixture = adminView();
      fixture.componentInstance.playerId.set('NewOne');
      fixture.componentInstance.label.set('main');
      fixture.detectChanges();

      expect(text(fixture)).toContain('GAME_ACCOUNTS.ERROR_LABEL_TAKEN');
      fixture.componentInstance.add();
      expect(service.add).not.toHaveBeenCalled();
    });

    it('can relabel and unlink whatever self-service says', () => {
      const fixture = adminView();

      expect(query(fixture, 'input.account-label')).not.toBeNull();
      expect(query(fixture, '.remove-button')).not.toBeNull();
      expect(query(fixture, '.link-elsewhere')).toBeNull();
    });
  });

  describe('writes', () => {
    it('relabels by handle and confirms it saved, only when the label changed', () => {
      const component = create({ selfService: true }).componentInstance;

      component.relabel(account('h-main', 'Main'), 'Main');
      expect(service.relabel).not.toHaveBeenCalled();

      component.relabel(account('h-main', 'Main'), 'Renamed');
      expect(service.relabel).toHaveBeenCalledWith('h-main', 'Renamed', undefined);
      expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.LABEL_SAVED', undefined, { duration: 2000 });
    });

    it('unlinks by handle and offers Undo, which restores from the sealed token', () => {
      const component = create({ admin: true, data: { displayName: 'Ash', userId: '42' } }).componentInstance;

      component.unlink(account('h-main', 'Main'), 0);
      expect(service.remove).toHaveBeenCalledWith('h-main', '42');
      expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.UNLINKED', 'GAME_ACCOUNTS.UNDO', { duration: 6000 });

      undo.next();
      expect(service.restore).toHaveBeenCalledWith('sealed-token', '42');
      expect(service.add).not.toHaveBeenCalled();
    });

    it('writes nothing when the viewer may not edit', () => {
      const component = create({ selfService: false }).componentInstance;

      component.unlink(account('h-main', 'Main'), 0);
      component.relabel(account('h-main', 'Main'), 'x');

      expect(service.remove).not.toHaveBeenCalled();
      expect(service.relabel).not.toHaveBeenCalled();
    });
  });
});
