import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminDestinationsComponent } from './admin-destinations.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';

/** A row as the API sends it: `kind` is the server's verdict, stamped from the type. */
function human(id: string, overrides: Partial<AdminUser> = {}): AdminUser {
  const type = overrides.type === undefined ? 'discord:user' : overrides.type;
  const person = !type || !type.trim() || type.trim().toLowerCase().endsWith(':user');
  return {
    id,
    name: id,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    kind: person ? 'person' : 'destination',
    language: null,
    lastChecked: null,
    notes: null,
    type,
    ...overrides,
  };
}

/**
 * Every global admin can manage every webhook. Listing them as delegates on each row put the same five
 * locked avatars on a hook nobody had been granted, and the real grants got lost among them.
 */
describe('AdminDestinationsComponent delegates column', () => {
  const hookA = human('https://hook/a', { name: 'Hook A', type: 'webhook' });
  const hookB = human('https://hook/b', { name: 'Hook B', type: 'webhook' });

  function setup() {
    const adminService = {
      getAllWebhookDelegates: jest.fn(() => of({ [hookA.id]: ['sam'] })),
      getPoracleAdmins: jest.fn(() => of(['admin1', 'admin2'])),
      getPorocleDelegates: jest.fn(() => of({ [hookA.id]: ['riley'] })),
      getUsers: jest.fn(() => of([hookA, hookB, human('sam'), human('riley'), human('admin1'), human('admin2')])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        {
          provide: AuthService,
          useValue: { impersonate: jest.fn(), isImpersonating: signal(false), user: signal({ id: 'admin1', isAdmin: true }) },
        },
      ],
      imports: [AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    TestBed.overrideProvider(MatDialog, { useValue: { open: jest.fn() } });
    const fixture = TestBed.createComponent(AdminDestinationsComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('lists the real grants, config and local, and not the global admins', () => {
    const fixture = setup();
    const ids = fixture.componentInstance.getDelegateUserIds(hookA.id);

    expect(ids).toEqual(['riley', 'sam']);
    expect(ids).not.toContain('admin1');
  });

  it('shows an empty column, not the admins, on a hook nobody was granted', () => {
    const fixture = setup();

    expect(fixture.componentInstance.getDelegateUserIds(hookB.id)).toEqual([]);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row');
    const hookBRow = Array.from(rows).find(r => r.textContent?.includes('Hook B'))!;
    expect(hookBRow.querySelector('[data-test="no-delegates"]')).toBeTruthy();
  });

  it('says once, in the header, that admins can manage every webhook', () => {
    const fixture = setup();
    const note = (fixture.nativeElement as HTMLElement).querySelector('[data-test="admins-note"]');

    expect(note?.textContent).toContain('ADMIN.DESTINATIONS_ADMINS_NOTE');
  });

  // .page-description is hidden under 600px, and on a phone the column would then read "No delegate grants"
  // on every hook with nothing saying admins still have access.
  it('keeps that note out of the page description, which phones hide', () => {
    const fixture = setup();
    const note = (fixture.nativeElement as HTMLElement).querySelector('[data-test="admins-note"]');

    expect(note?.closest('.page-description')).toBeNull();
    expect(note?.classList.contains('page-description')).toBe(false);
  });
});

/**
 * The Add dialog saves the destination itself and closes only on success, so a refusal keeps the admin's
 * input (its reasons are tested on the dialog). The page announces the new row, reloads, and shows it.
 */
describe('AdminDestinationsComponent add', () => {
  const created = { id: '1200000000000000009', name: 'New raids', type: 'discord:channel' };

  function setup(closedWith: unknown, rows: AdminUser[][] = [[]]) {
    const snack = { open: jest.fn() };
    let load = 0;
    const adminService = {
      createDestination: jest.fn(() => of(undefined)),
      getAllWebhookDelegates: jest.fn(() => of({})),
      getPoracleAdmins: jest.fn(() => of([])),
      getPorocleDelegates: jest.fn(() => of({})),
      getUsers: jest.fn(() => of(rows[Math.min(load++, rows.length - 1)])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        {
          provide: AuthService,
          useValue: { impersonate: jest.fn(), isImpersonating: signal(false), user: signal({ id: 'admin1', isAdmin: true }) },
        },
      ],
      imports: [AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snack });
    TestBed.overrideProvider(MatDialog, { useValue: { open: jest.fn(() => ({ afterClosed: () => of(closedWith) })) } });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ADMIN: { SNACK_DESTINATION_ADDED: 'Destination "{{name}}" added' }, TOAST: { OK: 'OK' } });
    translate.use('en');
    const fixture = TestBed.createComponent(AdminDestinationsComponent);
    fixture.detectChanges();
    fixture.componentInstance.openAddDialog();
    fixture.detectChanges();
    return { adminService, component: fixture.componentInstance, fixture, snack };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });

  it('announces the new destination and reloads, leaving the save to the dialog', () => {
    const { adminService, snack } = setup(created);

    expect(adminService.createDestination).not.toHaveBeenCalled();
    expect(snack.open).toHaveBeenCalledWith('Destination "New raids" added', 'OK', { duration: 3000 });
    expect(adminService.getUsers).toHaveBeenCalledTimes(2);
  });

  it('does nothing when the dialog is cancelled', () => {
    const { adminService, snack } = setup(null);

    expect(snack.open).not.toHaveBeenCalled();
    expect(adminService.getUsers).toHaveBeenCalledTimes(1);
  });

  it('turns to the page holding the new row and marks it', () => {
    const before = Array.from({ length: 30 }, (_, i) =>
      human(`11000000000000000${String(i).padStart(2, '0')}`, { type: 'discord:channel' }),
    );
    const after = [...before, human(created.id, { name: created.name, type: 'discord:channel' })];
    const { component, fixture } = setup(created, [before, after]);

    expect(component.list.pageIndex()).toBe(1);
    const marked = (fixture.nativeElement as HTMLElement).querySelector('tr.just-added');
    expect(marked?.textContent).toContain('New raids');
  });

  describe('scrolling the new row into view', () => {
    const before = Array.from({ length: 30 }, (_, i) =>
      human(`11000000000000000${String(i).padStart(2, '0')}`, { type: 'discord:channel' }),
    );
    const after = [...before, human(created.id, { name: created.name, type: 'discord:channel' })];
    let scrolled: { row: Element; options: ScrollIntoViewOptions | undefined }[];
    const originalScroll = Element.prototype.scrollIntoView;
    const originalMatchMedia = window.matchMedia;

    beforeEach(() => {
      scrolled = [];
      Element.prototype.scrollIntoView = function (this: Element, options?: boolean | ScrollIntoViewOptions) {
        scrolled.push({ options: options as ScrollIntoViewOptions, row: this });
      };
    });

    afterEach(() => {
      Element.prototype.scrollIntoView = originalScroll;
      window.matchMedia = originalMatchMedia;
    });

    function prefersReducedMotion(reduce: boolean): void {
      window.matchMedia = jest.fn((query: string) => ({ matches: reduce && query.includes('reduce') }) as MediaQueryList);
    }

    // At 1440x900 a 25-row page put the highlighted row below the fold, so the admin saw a snackbar and nothing else.
    it('scrolls the highlighted row into view, smoothly', () => {
      prefersReducedMotion(false);
      const { fixture } = setup(created, [before, after]);
      fixture.detectChanges();

      expect(scrolled).toHaveLength(1);
      expect(scrolled[0].row.textContent).toContain('New raids');
      expect(scrolled[0].options).toEqual({ behavior: 'smooth', block: 'center' });
    });

    it('jumps rather than glides when the viewer asks for reduced motion', () => {
      prefersReducedMotion(true);
      const { fixture } = setup(created, [before, after]);
      fixture.detectChanges();

      expect(scrolled).toHaveLength(1);
      expect(scrolled[0].options).toEqual({ behavior: 'auto', block: 'center' });
    });

    it('scrolls nothing when the dialog is cancelled', () => {
      prefersReducedMotion(false);
      const { fixture } = setup(null, [before]);
      fixture.detectChanges();

      expect(scrolled).toHaveLength(0);
    });
  });
});

describe('AdminDestinationsComponent list', () => {
  const token = 'abcdefghijklmnopqrstuvwxyz';
  const hook = human(`https://discord.com/api/webhooks/1234567890/${token}`, { name: 'Raid hook', type: 'webhook' });
  const channel = human('1100000000000000001', { name: 'raids', notes: '"PGAN / Raids"', type: 'discord:channel' });
  const group = human('-1001234567890', { name: 'North group', type: 'telegram:group' });
  const odd = human('42', { name: 'Typo row', type: 'x:unknown' });
  const sam = human('222', { name: 'Sam' });
  const legacy = human('333', { name: 'Legacy', type: null });

  function setup(users: AdminUser[] = [hook, channel, group, odd, sam, legacy]) {
    const dialog = { open: jest.fn(() => ({ afterClosed: () => of(false) })) };
    const adminService = {
      disableUser: jest.fn((id: string) => of(human(id, { adminDisable: 1 }))),
      enableUser: jest.fn((id: string) => of(human(id, { adminDisable: 0 }))),
      getAllWebhookDelegates: jest.fn(() => of({ [hook.id]: ['222'] })),
      getPoracleAdmins: jest.fn(() => of([] as string[])),
      getPorocleDelegates: jest.fn(() => of({})),
      getUsers: jest.fn(() => of(users)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        {
          provide: AuthService,
          useValue: { impersonate: jest.fn(), isImpersonating: signal(false), user: signal({ id: 'admin1', isAdmin: true }) },
        },
      ],
      imports: [AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    const fixture = TestBed.createComponent(AdminDestinationsComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const rowFor = (name: string) => Array.from(root.querySelectorAll('tr.mat-mdc-row')).find(r => r.textContent?.includes(name))!;
    /** Opens the row's menu and reads what it offers, by visible label and by test hook. */
    const menuOf = (name: string) => {
      (rowFor(name).querySelector('[data-test="row-menu"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      const items = Array.from(document.querySelectorAll('.mat-mdc-menu-panel [mat-menu-item]'));
      const result = {
        labels: items.map(b => b.querySelector('.mat-mdc-menu-item-text')?.textContent?.trim() ?? ''),
        tests: items.map(b => b.getAttribute('data-test')),
      };
      document.querySelector('.cdk-overlay-container')?.replaceChildren();
      return result;
    };
    return { adminService, component: fixture.componentInstance, dialog, fixture, menuOf, root, rowFor };
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => TestBed.resetTestingModule());

  it('lists every destination row and no person', () => {
    const { component, root } = setup();

    expect(component.list.filtered().map(r => r.id)).toEqual([hook.id, channel.id, group.id, odd.id]);
    expect(root.querySelectorAll('tr.mat-mdc-row')).toHaveLength(4);
    expect(root.textContent).not.toContain('Sam');
    expect(root.textContent).not.toContain('Legacy');
  });

  it('shows the raw type for an unknown kind', () => {
    const { rowFor } = setup();

    const typeCell = (name: string) => rowFor(name).querySelector('.mat-column-type')!;
    expect(typeCell('Typo row').textContent).toContain('x:unknown');
    expect(typeCell('Typo row').querySelector('mat-icon')?.textContent?.trim()).toBe('help_outline');
    expect(typeCell('raids').textContent).toContain('ADMIN.TYPE_DISCORD_CHANNEL');
    expect(typeCell('raids').querySelector('mat-icon')?.textContent?.trim()).toBe('tag');
    expect(typeCell('North group').querySelector('mat-icon')?.textContent?.trim()).toBe('send');
  });

  it('offers type chips only for types present', () => {
    const { root } = setup();

    const chips = Array.from(root.querySelectorAll('[data-test="type-chips"] [data-test="chip-label"]')).map(c => c.textContent?.trim());
    expect(chips).toEqual(['ADMIN.TYPE_ALL', 'ADMIN.TYPE_DISCORD_CHANNEL', 'ADMIN.TYPE_TELEGRAM_GROUP', 'ADMIN.TYPE_WEBHOOK', 'x:unknown']);
  });

  it('hides the type chips when every destination is one type', () => {
    const { root } = setup([hook, sam]);

    expect(root.querySelector('[data-test="type-chips"]')).toBeNull();
  });

  it('searches notes', () => {
    const { component, fixture, root } = setup();

    component.onSearchChange('pgan / raids');
    fixture.detectChanges();
    expect(component.list.filtered().map(r => r.id)).toEqual([channel.id]);
    expect(root.textContent).toContain('PGAN / Raids');
  });

  it('shows managers on every destination row', () => {
    const { rowFor } = setup();

    const managers = (name: string) => rowFor(name).querySelector('.delegate-avatars');
    for (const name of ['Raid hook', 'raids', 'North group', 'Typo row']) {
      expect(managers(name)).toBeTruthy();
    }
    expect(rowFor('raids').querySelector('[data-test="no-delegates"]')).toBeTruthy();
  });

  it('offers to manage delegates only on the six kinds a grant can name', () => {
    // The server refuses a grant on an unknown type, so the button would open a dialog whose every save
    // fails. A topic is one of the six and keeps it, as does a type stored in another case.
    const topic = human('-1001234567890:42', { name: 'Topic row', type: 'telegram:topic' });
    const shouty = human('1100000000000000002', { name: 'Shouty row', type: 'Discord:Thread' });
    const { menuOf } = setup([hook, channel, group, odd, topic, shouty]);

    const manage = (name: string) => menuOf(name).tests.includes('manage-delegates');
    expect(manage('Typo row')).toBe(false);
    for (const name of ['Raid hook', 'raids', 'North group', 'Topic row', 'Shouty row']) {
      expect(manage(name)).toBe(true);
    }
  });

  // Seven icon buttons ran 220px past the container at 1440 with no scroll cue (#1025 QA).
  it('keeps Manage alarms on the row and names every other action in the row menu', () => {
    const { component, menuOf, rowFor } = setup();

    const row = rowFor('Raid hook');
    const inline = Array.from(row.querySelectorAll('.action-buttons > button')).map(b => b.getAttribute('data-test'));
    expect(inline).toEqual(['manage-alarms', 'row-menu']);
    expect(row.querySelector('[data-test="manage-alarms"]')?.textContent).toContain('ADMIN.MANAGE_ALARMS');
    expect(row.querySelector('[data-test="row-menu"]')?.getAttribute('aria-label')).toBe('ADMIN.MORE_ACTIONS_FOR');

    expect(menuOf('Raid hook').labels).toEqual([
      'ADMIN.BLOCK_DESTINATION',
      'ADMIN.STOP_ALERTS',
      'ADMIN.MANAGE_DELEGATES',
      'ADMIN.DELETE_ALL_ALARMS',
      'ADMIN.DELETE_DESTINATION',
    ]);
    expect(menuOf('North group').labels).toEqual([
      'ADMIN.BLOCK_DESTINATION',
      'ADMIN.STOP_ALERTS',
      'ADMIN.MANAGE_DELEGATES',
      'ADMIN.DELETE_ALL_ALARMS',
      'ADMIN.DELETE_DESTINATION',
    ]);

    const manage = jest.spyOn(component, 'manageAlarms').mockImplementation(() => undefined);
    (row.querySelector('[data-test="manage-alarms"]') as HTMLButtonElement).click();
    expect(manage).toHaveBeenCalledWith(hook);
  });

  // Polish needs the neuter for "miejsce docelowe", so the badges cannot share Users' masculine keys.
  it('labels status with its own keys, not the ones Users shares', () => {
    const { rowFor } = setup([
      human('-1', { name: 'On group', type: 'telegram:group' }),
      human('-2', { name: 'Paused group', enabled: 0, type: 'telegram:group' }),
      human('-3', { name: 'Shut group', adminDisable: 1, type: 'telegram:group' }),
    ]);

    const status = (name: string) => rowFor(name).querySelector('.mat-column-enabled')?.textContent?.trim();
    expect(status('On group')).toBe('ADMIN.DESTINATION_STATUS_ACTIVE');
    expect(status('Paused group')).toBe('ADMIN.DESTINATION_STATUS_STOPPED');
    expect(status('Shut group')).toBe('ADMIN.DESTINATION_STATUS_BLOCKED');
  });

  it('offers Unblock and Start alerts where they apply', () => {
    const { menuOf } = setup([human('-100', { name: 'Quiet group', adminDisable: 1, enabled: 0, type: 'telegram:group' })]);

    expect(menuOf('Quiet group').labels).toEqual([
      'ADMIN.UNBLOCK_DESTINATION',
      'ADMIN.START_ALERTS',
      'ADMIN.MANAGE_DELEGATES',
      'ADMIN.DELETE_ALL_ALARMS',
      'ADMIN.DELETE_DESTINATION',
    ]);
  });

  it('names the destination it blocked or unblocked, not a user', () => {
    const { component } = setup();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ADMIN: { SNACK_DESTINATION_BLOCKED: '"{{name}}" blocked', SNACK_DESTINATION_UNBLOCKED: '"{{name}}" unblocked' },
    });
    translate.use('en');
    const snack = TestBed.inject(MatSnackBar) as unknown as { open: jest.Mock };

    component.toggleBlock(group, false);
    expect(snack.open.mock.calls.at(-1)?.[0]).toBe('"North group" blocked');
    component.toggleBlock(group, true);
    expect(snack.open.mock.calls.at(-1)?.[0]).toBe('"North group" unblocked');
  });

  it('never shows a webhook token, in the ID cell or anywhere else in the row', () => {
    const { rowFor } = setup();

    const cell = rowFor('Raid hook').querySelector('.mat-column-id')!;
    expect(cell.textContent).toContain('discord.com/api/webhooks/1234567890');
    expect(rowFor('Raid hook').innerHTML).not.toContain(token);
  });

  it('shortens any webhook URL to its host and webhook number, dropping what follows', () => {
    const { component } = setup();

    expect(component.truncateUrl('https://discord.com/api/webhooks/1234567890/secret-token')).toBe('discord.com/api/webhooks/1234567890…');
    expect(component.truncateUrl('https://discordapp.com/api/webhooks/42/secret-token?wait=true')).toBe('discordapp.com/api/webhooks/42…');
    expect(component.truncateUrl('http://10.0.0.5:4000/hooks/secret-token')).toBe('10.0.0.5:4000/…');
    expect(component.truncateUrl('discord.com/api/webhooks/7/secret-token')).toBe('discord.com/api/webhooks/7…');
    expect(component.truncateUrl('not a url secret-token')).not.toContain('secret');
  });

  it('never names a nameless webhook by its URL', () => {
    const { component } = setup();
    const nameless = human('https://discord.com/api/webhooks/9/secret-token', { name: '', type: 'webhook' });

    expect(component.rowName(nameless)).not.toContain('secret');
    expect(component.rowName(group)).toBe('North group');
    expect(component.rowName(human('-5', { name: '', type: 'telegram:group' }))).toBe('-5');
  });

  it('shortens a webhook URL, shows any other id whole, and copies either', () => {
    const { component, rowFor } = setup();

    expect(rowFor('Raid hook').querySelector('.mat-column-id')?.textContent).toContain('discord.com');
    expect(rowFor('Raid hook').querySelector('.mat-column-id')?.textContent).not.toContain(hook.id);
    expect(rowFor('North group').querySelector('.mat-column-id')?.textContent).toContain('-1001234567890');

    const copy = jest.spyOn(component, 'copyId').mockImplementation(() => undefined);
    (rowFor('North group').querySelector('[data-test="copy-id"]') as HTMLButtonElement).click();
    expect(copy).toHaveBeenCalledWith(group.id);
    (rowFor('Raid hook').querySelector('[data-test="copy-id"]') as HTMLButtonElement).click();
    expect(copy).toHaveBeenCalledWith(hook.id);
    expect(rowFor('North group').querySelector('[data-test="copy-id"]')?.getAttribute('aria-label')).toBe('ADMIN.COPY_ID_FOR');
  });

  it('names each copy button after its row, falling back to the id when unnamed', () => {
    const { rowFor } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { ADMIN: { COPY_ID_FOR: 'Copy ID of {{name}}' } });
    TestBed.inject(TranslateService).use('en');

    const label = (name: string) => rowFor(name).querySelector('[data-test="copy-id"]')?.getAttribute('aria-label');
    TestBed.inject(ApplicationRef).tick();
    expect(label('North group')).toBe('Copy ID of North group');
    expect(label('Raid hook')).toBe('Copy ID of Raid hook');
  });

  it('goes back to the first page when the type or status filter changes', () => {
    const { component } = setup();

    component.list.setPage(2, 10);
    component.onTypeFilterChange('webhook');
    expect(component.list.pageIndex()).toBe(0);

    component.list.setPage(2, 10);
    component.onStatusFilterChange('blocked');
    expect(component.list.pageIndex()).toBe(0);
  });

  it('keeps its paging and type choice apart from Users', () => {
    const { component } = setup();

    component.onTypeFilterChange('telegram:group');
    component.list.setPage(1, 10);
    expect(sessionStorage.getItem('admin_destinations_type')).toBe('telegram:group');
    expect(sessionStorage.getItem('admin_destinations_pageSize')).toBe('10');
    expect(sessionStorage.getItem('admin_type')).toBeNull();
  });

  it('says on a channel delete that the channel itself is left alone, and not on a webhook', () => {
    const { component, dialog } = setup();
    const message = () => ((dialog.open.mock.calls.at(-1) as unknown[])[1] as { data: { message: string } }).data.message;

    component.deleteDestination(group);
    expect(message()).toContain('ADMIN.DELETE_DESTINATION_KIND_NOTE');

    component.deleteDestination(hook);
    expect(message()).not.toContain('NOTE');
  });

  it('names the kind it leaves alone, translated, and falls back to the generic note for an unknown type', () => {
    const { component, dialog } = setup();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ADMIN: {
        DELETE_DESTINATION_KIND_NOTE: 'The {{kind}} itself isn’t touched.',
        DELETE_DESTINATION_NOTE: 'The Discord or Telegram channel itself isn’t touched.',
        TYPE_DISCORD_CHANNEL: 'Discord channel',
        TYPE_TELEGRAM_GROUP: 'Telegram group',
      },
    });
    translate.use('en');
    const message = () => ((dialog.open.mock.calls.at(-1) as unknown[])[1] as { data: { message: string } }).data.message;

    component.deleteDestination(group);
    expect(message()).toContain('The Telegram group itself isn’t touched.');
    expect(message()).not.toContain('Discord');

    component.deleteDestination(channel);
    expect(message()).toContain('The Discord channel itself isn’t touched.');

    component.deleteDestination(odd);
    expect(message()).toContain('The Discord or Telegram channel itself isn’t touched.');
  });

  it('explains on a channel that Poracle blocks one it can no longer find', () => {
    const { component } = setup();

    expect(component.blockTooltipKey(group)).toBe('ADMIN.BLOCKED_CHANNEL_TOOLTIP');
    expect(component.blockTooltipKey(hook)).toBe('ADMIN.BLOCK_DESTINATION');
  });
});
