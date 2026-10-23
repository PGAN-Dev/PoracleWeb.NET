import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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

describe('AddDestinationDialogComponent', () => {
  function setup() {
    const dialogRef = { close: jest.fn() };
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MatDialogRef, useValue: dialogRef }],
      imports: [AddDestinationDialogComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AddDestinationDialogComponent);
    fixture.detectChanges();
    return { component: fixture.componentInstance, dialogRef, fixture };
  }

  function pick(component: AddDestinationDialogComponent, kind: string): void {
    component.form.controls.kind.setValue(kind);
  }

  afterEach(() => TestBed.resetTestingModule());

  it('asks for a URL for a webhook and an id for a Discord channel', () => {
    const { component, fixture } = setup();
    const root = fixture.nativeElement as HTMLElement;
    const idLabel = () => root.querySelector('[data-test="id-field"] mat-label')?.textContent?.trim();

    pick(component, 'webhook');
    fixture.detectChanges();
    expect(idLabel()).toBe('ADMIN.WEBHOOK_URL');
    component.form.controls.id.setValue('1100000000000000001');
    expect(component.form.controls.id.hasError('url')).toBe(true);

    pick(component, 'discord:channel');
    fixture.detectChanges();
    expect(idLabel()).toBe('ADMIN.ID_DISCORD');
    component.form.controls.id.setValue('https://discord.com/api/webhooks/1/abc');
    expect(component.form.controls.id.hasError('digits')).toBe(true);
    component.form.controls.id.setValue('1100000000000000001');
    expect(component.form.controls.id.valid).toBe(true);
  });

  /** The server accepts both, and existing webhooks use http. */
  it.each(['https://discord.com/api/webhooks/1/abc', 'http://10.0.0.5:8080/hook'])('accepts the webhook URL %s', url => {
    const { component } = setup();
    pick(component, 'webhook');
    component.form.patchValue({ id: url, name: 'Hook' });

    expect(component.form.valid).toBe(true);
  });

  it('joins a Telegram topic\u2019s chat and topic ids', () => {
    const { component, dialogRef } = setup();
    pick(component, 'telegram:topic');
    component.form.patchValue({ name: 'Raids topic', chatId: '-1001234567890', topicId: '42' });

    component.submit();

    expect(dialogRef.close).toHaveBeenCalledWith({ id: '-1001234567890:42', name: 'Raids topic', type: 'telegram:topic' });
  });

  it('refuses a positive Telegram chat id', () => {
    const { component } = setup();
    pick(component, 'telegram:group');
    component.form.patchValue({ id: '1001234567890', name: 'Group' });

    expect(component.form.controls.id.hasError('negativeChat')).toBe(true);
    expect(component.form.valid).toBe(false);
  });

  /** A supergroup's -100... id and a legacy basic group's shorter one are both real. */
  it.each(['-1001234567890', '-1234567890'])('accepts the Telegram chat id %s', id => {
    const { component } = setup();
    pick(component, 'telegram:group');
    component.form.patchValue({ id, name: 'Group' });

    expect(component.form.valid).toBe(true);
  });

  it('refuses a topic number of zero', () => {
    const { component } = setup();
    pick(component, 'telegram:topic');
    component.form.patchValue({ name: 'Topic', chatId: '-1001234567890', topicId: '0' });

    expect(component.form.controls.topicId.hasError('topicPositive')).toBe(true);
  });

  it('keeps the name and clears the id when the kind changes', () => {
    const { component } = setup();
    pick(component, 'discord:channel');
    component.form.patchValue({ id: '1100000000000000001', name: 'raids' });

    pick(component, 'telegram:channel');

    expect(component.form.controls.name.value).toBe('raids');
    expect(component.form.controls.id.value).toBe('');
  });

  it('sends a note only when one was written, trimmed', () => {
    const { component, dialogRef } = setup();
    pick(component, 'discord:thread');
    component.form.patchValue({ id: ' 1100000000000000002 ', name: ' raid chat ', notes: '  ' });
    component.submit();
    expect(dialogRef.close).toHaveBeenLastCalledWith({ id: '1100000000000000002', name: 'raid chat', type: 'discord:thread' });

    component.form.patchValue({ notes: ' PGAN / Raids ' });
    component.submit();
    expect(dialogRef.close).toHaveBeenLastCalledWith({
      id: '1100000000000000002',
      name: 'raid chat',
      notes: 'PGAN / Raids',
      type: 'discord:thread',
    });
  });

  it('stays open until a kind is chosen', () => {
    const { component, dialogRef } = setup();
    component.form.patchValue({ name: 'Hook' });

    component.submit();

    expect(component.form.valid).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
