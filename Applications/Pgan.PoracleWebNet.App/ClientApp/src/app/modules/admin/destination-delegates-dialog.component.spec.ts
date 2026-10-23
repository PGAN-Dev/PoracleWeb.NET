import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AddDestinationDialogComponent, DestinationDelegatesDialogComponent } from './admin-destinations.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';

function user(id: string, name: string, type = 'discord:user'): AdminUser {
  return {
    id,
    name,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    kind: type === 'discord:user' ? 'person' : 'destination',
    language: null,
    lastChecked: null,
    notes: null,
    type,
  };
}

describe('DestinationDelegatesDialogComponent', () => {
  const webhook = user('https://example.com/hook', 'Hook', 'webhook');
  const sam = user('222', 'Sam');

  function setup(allUsers: AdminUser[] = [webhook, sam]) {
    const adminService = {
      addWebhookDelegate: jest.fn(() => of(['222'])),
      getWebhookDelegates: jest.fn(() => of([] as string[])),
      removeWebhookDelegate: jest.fn(() => of([] as string[])),
    };
    const snackBar = { open: jest.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { allUsers, poracleAdmins: [], porocleDelegates: [], webhook } },
      ],
      imports: [DestinationDelegatesDialogComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });

    const fixture = TestBed.createComponent(DestinationDelegatesDialogComponent);
    fixture.detectChanges();
    return { adminService, component: fixture.componentInstance, fixture, snackBar };
  }

  afterEach(() => TestBed.resetTestingModule());

  // A channel or an unknown type is not somebody who can sign in, so granting it a destination grants nobody.
  it('offers only people as delegates', () => {
    const { component } = setup([
      webhook,
      sam,
      user('-1001234567890', 'Sam group', 'telegram:group'),
      user('333', 'Sam channel', 'discord:channel'),
      user('444', 'Sam odd', 'x:unknown'),
      { ...user('555', 'Sam legacy'), type: null },
    ]);

    component.onSearchChange('sam');

    expect(component.filteredUsers().map(u => u.id)).toEqual(['222', '555']);
  });

  /** "Sam" was sent as a user id and refused with a 400 behind a generic toast. */
  it('does not send free text as a user id', () => {
    const { adminService, component } = setup();
    component.searchText = 'Sam';

    expect(component.pickedUserId).toBeNull();
    component.addDelegateFromSearch();

    expect(adminService.addWebhookDelegate).not.toHaveBeenCalled();
  });

  it('disables Add until the field names a user from the list', async () => {
    const { fixture } = setup();
    const root = fixture.nativeElement as HTMLElement;
    const addButton = () => root.querySelector('.add-row button') as HTMLButtonElement;
    const type = async (text: string) => {
      const input = root.querySelector('.add-row input') as HTMLInputElement;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();
    };

    await type('Sam');
    expect(addButton().disabled).toBe(true);

    await type('222');
    expect(addButton().disabled).toBe(false);
  });

  it('adds a user picked from the list', () => {
    const { adminService, component } = setup();

    component.onUserSelected('222');

    expect(adminService.addWebhookDelegate).toHaveBeenCalledWith(webhook.id, '222');
  });

  it('adds the user whose id the field holds', () => {
    const { adminService, component } = setup();
    component.searchText = ' 222 ';

    component.addDelegateFromSearch();

    expect(adminService.addWebhookDelegate).toHaveBeenCalledWith(webhook.id, '222');
  });

  it('leaves Enter on a highlighted option to the autocomplete, which has already added it', () => {
    const { adminService, component } = setup();
    component.searchText = '222';
    const event = new KeyboardEvent('keydown', { cancelable: true, key: 'Enter' });
    event.preventDefault();

    component.addDelegateFromSearch(event);

    expect(adminService.addWebhookDelegate).not.toHaveBeenCalled();
  });

  it('shows the server’s reason when the grant is refused', () => {
    const { adminService, component, snackBar } = setup();
    adminService.addWebhookDelegate.mockReturnValue(throwError(() => ({ error: { error: 'Already a delegate.' }, status: 409 })));

    component.onUserSelected('222');

    expect(snackBar.open).toHaveBeenCalledWith('Already a delegate.', expect.anything(), expect.anything());
  });
});

/**
 * #1025 QA: the remove button sat at tabindex -1 inside a presentational chip set, so nobody on a keyboard
 * could take a grant away. And the dialog listed every admin as a locked chip while the page beside it said
 * admins are not listed, because they manage every destination without a grant.
 */
describe('DestinationDelegatesDialogComponent chips', () => {
  const webhook = user('https://example.com/hook', 'Hook', 'webhook');

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        {
          provide: AdminService,
          useValue: { getWebhookDelegates: jest.fn(() => of(['222'])), removeWebhookDelegate: jest.fn(() => of([] as string[])) },
        },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            allUsers: [webhook, user('222', 'Sam'), user('admin1', 'Ada Admin'), user('333', 'Riley')],
            porocleDelegates: ['333'],
            webhook,
          },
        },
      ],
      imports: [DestinationDelegatesDialogComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ADMIN: { REMOVE_DELEGATE_NAMED: 'Remove {{name}}' } });
    translate.use('en');
    const fixture = TestBed.createComponent(DestinationDelegatesDialogComponent);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('puts each grant in a grid row whose remove button is a named grid cell', () => {
    const { root } = setup();

    const grid = root.querySelector('[role="grid"]');
    expect(grid).toBeTruthy();
    const remove = grid!.querySelector('[data-test="remove-delegate"]')!;
    expect(remove.getAttribute('aria-label')).toBe('Remove Sam (222)');
    // The grid's key manager moves focus between cells, so tabindex -1 on the button is roving, not unreachable.
    expect(remove.closest('[role="gridcell"]')?.closest('[role="row"]')?.closest('[role="grid"]')).toBe(grid);
  });

  it('lists config grants, locked, and leaves out admins, who need no grant', () => {
    const { root } = setup();

    expect(root.textContent).toContain('Riley (333)');
    expect(root.textContent).not.toContain('Ada Admin');
    expect(root.querySelectorAll('[data-test="remove-delegate"]')).toHaveLength(1);
  });

  it('marks each avatar decorative, since the name beside it already says who it is', () => {
    TestBed.overrideProvider(MAT_DIALOG_DATA, {
      useValue: {
        allUsers: [
          webhook,
          { ...user('222', 'Sam'), avatarUrl: 'https://cdn.example/sam.png' },
          { ...user('333', 'Riley'), avatarUrl: 'https://cdn.example/riley.png' },
        ],
        porocleDelegates: ['333'],
        webhook,
      },
    });
    const { root } = setup();

    const avatars = Array.from(root.querySelectorAll('mat-chip-row img'));
    expect(avatars).toHaveLength(2);
    for (const img of avatars) {
      // Material's matChipAvatar sets role="img", which axe refuses on an empty alt (aria-allowed-role).
      expect(img.getAttribute('alt')).toBe('');
      expect(img.getAttribute('role')).toBe('presentation');
    }
  });
});

describe('AddDestinationDialogComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MatDialogRef, useValue: { close: jest.fn() } }],
      imports: [AddDestinationDialogComponent, NoopAnimationsModule],
    });
    return TestBed.createComponent(AddDestinationDialogComponent).componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  /** The server accepts both, and existing webhooks use http; the message now says so. */
  it.each(['https://discord.com/api/webhooks/1/abc', 'http://10.0.0.5:8080/hook'])('accepts %s', url => {
    const component = setup();
    component.form.setValue({ name: 'Hook', url });

    expect(component.form.valid).toBe(true);
  });

  it('refuses a URL without a scheme', () => {
    const component = setup();
    component.form.setValue({ name: 'Hook', url: 'example.com/hook' });

    expect(component.form.get('url')?.hasError('pattern')).toBe(true);
  });
});
