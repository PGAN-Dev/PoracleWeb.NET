import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

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
        { provide: AuthService, useValue: { impersonate: jest.fn(), user: signal({ id: 'admin1', isAdmin: true }) } },
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
 * #482: when PoracleNG refuses a webhook and the existence probe fails too, the API answers 502 telling the
 * admin to check the list before retrying, because a retry can sit a second row beside a half-written one.
 * That sentence has to reach the admin, not "Check the URL and try again".
 */
describe('AdminDestinationsComponent add failure', () => {
  function setup(error: unknown, translations?: Record<string, unknown>) {
    const snack = { open: jest.fn() };
    const adminService = {
      createDestination: jest.fn(() => throwError(() => error)),
      getAllWebhookDelegates: jest.fn(() => of({})),
      getPoracleAdmins: jest.fn(() => of([])),
      getPorocleDelegates: jest.fn(() => of({})),
      getUsers: jest.fn(() => of([])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: AuthService, useValue: { impersonate: jest.fn(), user: signal({ id: 'admin1', isAdmin: true }) } },
      ],
      imports: [AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snack });
    TestBed.overrideProvider(MatDialog, {
      useValue: {
        open: jest.fn(() => ({ afterClosed: () => of({ id: 'https://discord.com/api/webhooks/1/x', name: 'Hook', type: 'webhook' }) })),
      },
    });
    if (translations) {
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('en', translations);
      translate.use('en');
    }
    const fixture = TestBed.createComponent(AdminDestinationsComponent);
    fixture.detectChanges();
    fixture.componentInstance.openAddDialog();
    return snack;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the half-write warning the 502 carries', () => {
    const warning =
      'Poracle rejected the destination, and could not be asked whether part of it was saved. Check the destination list before trying again.';
    const snack = setup({ error: { error: warning }, status: 502 });

    expect(snack.open).toHaveBeenCalledWith(warning, 'TOAST.OK', { duration: 4000 });
  });

  it('shows "Nothing was created." when the API could confirm it', () => {
    const snack = setup({ error: { error: 'Poracle rejected the destination. Nothing was created.' }, status: 502 });

    expect(snack.open).toHaveBeenCalledWith('Poracle rejected the destination. Nothing was created.', 'TOAST.OK', { duration: 4000 });
  });

  it('keeps its own translated message for a server error that says nothing useful', () => {
    const snack = setup({ error: { error: 'An unexpected error occurred.' }, status: 500 });

    expect(snack.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_ADD_DESTINATION', 'TOAST.OK', { duration: 4000 });
  });

  it('shows who already holds the id on a 409', () => {
    const snack = setup(
      { error: { name: 'raids', error: 'A destination with this id already exists.', type: 'discord:channel' }, status: 409 },
      { ADMIN: { DESTINATION_EXISTS: 'Already in Poracle as "{{name}}" ({{type}}).', TYPE_DISCORD_CHANNEL: 'Discord channel' } },
    );

    expect(snack.open).toHaveBeenCalledWith('Already in Poracle as "raids" (Discord channel).', 'TOAST.OK', { duration: 4000 });
  });

  it('falls back to the server’s sentence on a 409 that could not name the holder', () => {
    const snack = setup({ error: { name: null, error: 'A destination with this id already exists.', type: null }, status: 409 });

    expect(snack.open).toHaveBeenCalledWith('A destination with this id already exists.', 'TOAST.OK', { duration: 4000 });
  });
});

describe('AdminDestinationsComponent list', () => {
  const hook = human('https://discord.com/api/webhooks/1/abcdefghijklmnopqrstuvwxyz', { name: 'Raid hook', type: 'webhook' });
  const channel = human('1100000000000000001', { name: 'raids', notes: '"PGAN / Raids"', type: 'discord:channel' });
  const group = human('-1001234567890', { name: 'North group', type: 'telegram:group' });
  const odd = human('42', { name: 'Typo row', type: 'x:unknown' });
  const sam = human('222', { name: 'Sam' });
  const legacy = human('333', { name: 'Legacy', type: null });

  function setup(users: AdminUser[] = [hook, channel, group, odd, sam, legacy]) {
    const dialog = { open: jest.fn(() => ({ afterClosed: () => of(false) })) };
    const adminService = {
      getAllWebhookDelegates: jest.fn(() => of({ [hook.id]: ['222'] })),
      getPoracleAdmins: jest.fn(() => of([] as string[])),
      getPorocleDelegates: jest.fn(() => of({})),
      getUsers: jest.fn(() => of(users)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: AuthService, useValue: { impersonate: jest.fn(), user: signal({ id: 'admin1', isAdmin: true }) } },
      ],
      imports: [AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    const fixture = TestBed.createComponent(AdminDestinationsComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const rowFor = (name: string) => Array.from(root.querySelectorAll('tr.mat-mdc-row')).find(r => r.textContent?.includes(name))!;
    return { component: fixture.componentInstance, dialog, fixture, root, rowFor };
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

    const manage = (name: string) => rowFor(name).querySelector('[data-test="manage-delegates"]');
    const managers = (name: string) => rowFor(name).querySelector('.delegate-avatars');
    for (const name of ['Raid hook', 'raids', 'North group', 'Typo row']) {
      expect(manage(name)).toBeTruthy();
      expect(managers(name)).toBeTruthy();
    }
    expect(rowFor('raids').querySelector('[data-test="no-delegates"]')).toBeTruthy();
  });

  it('shortens a webhook URL, shows any other id whole, and copies either', () => {
    const { component, rowFor } = setup();

    expect(rowFor('Raid hook').querySelector('.mat-column-id')?.textContent).toContain('discord.com');
    expect(rowFor('Raid hook').querySelector('.mat-column-id')?.textContent).not.toContain(hook.id);
    expect(rowFor('North group').querySelector('.mat-column-id')?.textContent).toContain('-1001234567890');

    const copy = jest.spyOn(component, 'copyId').mockImplementation(() => undefined);
    (rowFor('North group').querySelector('[data-test="copy-id"]') as HTMLButtonElement).click();
    expect(copy).toHaveBeenCalledWith(group.id);
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
    expect(message()).toContain('ADMIN.DELETE_DESTINATION_NOTE');

    component.deleteDestination(hook);
    expect(message()).not.toContain('ADMIN.DELETE_DESTINATION_NOTE');
  });

  it('explains on a channel that Poracle blocks one it can no longer find', () => {
    const { component } = setup();

    expect(component.blockTooltipKey(group)).toBe('ADMIN.BLOCKED_CHANNEL_TOOLTIP');
    expect(component.blockTooltipKey(hook)).toBe('ADMIN.BLOCK_DESTINATION');
  });
});
