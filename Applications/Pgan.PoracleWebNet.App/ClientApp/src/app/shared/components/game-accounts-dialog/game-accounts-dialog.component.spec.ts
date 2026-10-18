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
  };

  const account = (playerId: string, label: string | null = null): PlayerAccount => ({
    createdAt: '2026-10-01T00:00:00Z',
    label,
    playerId,
  });

  function create(
    opts: { admin?: boolean; data?: GameAccountsDialogData; impersonating?: boolean; selfService?: boolean } = {},
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
        { provide: AuthService, useValue: { isAdmin: () => opts.admin ?? false, isImpersonating: () => opts.impersonating ?? false } },
        { provide: SettingsService, useValue: { supportUrl } },
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
    accounts = signal<PlayerAccount[] | null>([account('AbC123', 'Main'), account('Zed9')]);
    loadFailed = signal(false);
    supportUrl = signal<string | null>(null);
    undo = new Subject<void>();
    snackBar = { open: jest.fn(() => ({ onAction: () => undo })) };
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

  it('lets an administrator edit their own accounts with self-service off, without blaming the setting', () => {
    const fixture = create({ admin: true, selfService: false });

    expect(query(fixture, '.add-form')).not.toBeNull();
    expect(query(fixture, '.remove-button')).not.toBeNull();
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.SELF_SERVICE_OFF');
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.IMPERSONATING');
  });

  it('is read-only while impersonating with self-service off, and says it is the impersonation', () => {
    const fixture = create({ impersonating: true, selfService: false });

    expect(query(fixture, '.add-form')).toBeNull();
    expect(text(fixture)).toContain('GAME_ACCOUNTS.IMPERSONATING');
    expect(text(fixture)).not.toContain('GAME_ACCOUNTS.SELF_SERVICE_OFF');
  });

  it('stays read-only for an administrator session that is impersonating', () => {
    const fixture = create({ admin: true, impersonating: true, selfService: false });

    expect(query(fixture, '.add-form')).toBeNull();
    expect(text(fixture)).toContain('GAME_ACCOUNTS.IMPERSONATING');
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

    component.playerId.set('AbCxYz9');
    component.label.set('Alt');
    component.add();

    expect(service.add).toHaveBeenCalledWith('AbCxYz9', 'Alt', undefined);
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

  describe('checks a player id before sending it', () => {
    function typed(playerId: string, label = '') {
      const fixture = create({ selfService: true });
      fixture.componentInstance.playerId.set(playerId);
      fixture.componentInstance.label.set(label);
      fixture.detectChanges();
      return fixture;
    }

    it('refuses anything but letters and numbers, at the field, without a request', () => {
      // PoracleNG's own rule (^[A-Za-z0-9]+$, at most 256); its refusal text is a regex.
      for (const bad of ['bad id', 'AbC_123', 'Ab-c', 'a'.repeat(257)]) {
        const fixture = typed(bad);
        expect(text(fixture)).toContain('GAME_ACCOUNTS.ERROR_INVALID');
        expect((query(fixture, '.add-button') as HTMLButtonElement).disabled).toBe(true);
        fixture.componentInstance.add();
        expect(service.add).not.toHaveBeenCalled();
      }
    });

    it('accepts an id of letters and numbers up to 256 long', () => {
      for (const ok of ['NewOne7', 'a'.repeat(256)]) {
        const fixture = typed(ok);
        expect(text(fixture)).not.toContain('GAME_ACCOUNTS.ERROR_INVALID');
        fixture.componentInstance.add();
        expect(service.add).toHaveBeenCalled();
      }
    });

    it('refuses an id that is already linked, matching case exactly', () => {
      let fixture = typed('AbC123');
      expect(text(fixture)).toContain('GAME_ACCOUNTS.ERROR_DUPLICATE');
      fixture.componentInstance.add();
      expect(service.add).not.toHaveBeenCalled();

      // Player ids are case-sensitive, so a different case is a different account.
      fixture = typed('abc123');
      expect(text(fixture)).not.toContain('GAME_ACCOUNTS.ERROR_DUPLICATE');
    });

    it('refuses a label another account already uses, ignoring case', () => {
      const fixture = typed('NewOne', 'main');

      expect(text(fixture)).toContain('GAME_ACCOUNTS.ERROR_LABEL_TAKEN');
      fixture.componentInstance.add();
      expect(service.add).not.toHaveBeenCalled();
    });

    it('warns that twelve digits look like a friend code, but still lets them through', () => {
      const fixture = typed('123456789012');

      expect(text(fixture)).toContain('GAME_ACCOUNTS.FRIEND_CODE_WARNING');
      fixture.componentInstance.add();
      expect(service.add).toHaveBeenCalledWith('123456789012', '', undefined);
    });

    it('says what a player id is before anything is typed', () => {
      expect(text(typed(''))).toContain('GAME_ACCOUNTS.PLAYER_ID_HINT');
    });
  });

  it('offers to undo an unlink, putting the label back', () => {
    const fixture = create({ data: { displayName: 'Ash', userId: '42' } });

    fixture.componentInstance.unlink('AbC123');
    expect(service.remove).toHaveBeenCalledWith('AbC123', '42');
    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.UNLINKED', 'GAME_ACCOUNTS.UNDO', { duration: 6000 });

    undo.next();
    expect(service.add).toHaveBeenCalledWith('AbC123', 'Main', '42');
  });

  it('confirms a saved label', () => {
    const fixture = create({ selfService: true });

    fixture.componentInstance.relabel(account('AbC123', 'Main'), 'Renamed');

    expect(snackBar.open).toHaveBeenCalledWith('GAME_ACCOUNTS.LABEL_SAVED', undefined, { duration: 2000 });
  });
});
