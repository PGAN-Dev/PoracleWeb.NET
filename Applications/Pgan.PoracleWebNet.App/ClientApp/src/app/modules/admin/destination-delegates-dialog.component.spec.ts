import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Observable, of, Subject, throwError } from 'rxjs';

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
  function setup(create: () => Observable<void> = () => of(undefined), translations?: Record<string, unknown>) {
    const dialogRef = { close: jest.fn(), disableClose: false };
    const adminService = { createDestination: jest.fn(create) };
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: AdminService, useValue: adminService },
      ],
      imports: [AddDestinationDialogComponent, NoopAnimationsModule],
    });
    if (translations) {
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('en', translations);
      translate.use('en');
    }
    const fixture = TestBed.createComponent(AddDestinationDialogComponent);
    fixture.detectChanges();
    return { adminService, component: fixture.componentInstance, dialogRef, fixture };
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
    expect(idLabel()).toBe('ADMIN.ID_DISCORD_CHANNEL');
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
    const { adminService, component, dialogRef } = setup();
    pick(component, 'telegram:topic');
    component.form.patchValue({ name: 'Raids topic', chatId: '-1001234567890', topicId: '42' });

    component.submit();

    expect(adminService.createDestination).toHaveBeenCalledWith({ id: '-1001234567890:42', name: 'Raids topic', type: 'telegram:topic' });
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

    // The dialog has closed by now, so the second body is read rather than sent.
    component.form.patchValue({ notes: ' PGAN / Raids ' });
    expect(component.request()).toEqual({
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

  it('labels the id by what it is: a channel, a thread, or a webhook URL', () => {
    const { component, fixture } = setup();
    const root = fixture.nativeElement as HTMLElement;
    const idLabel = () => root.querySelector('[data-test="id-field"] mat-label')?.textContent?.trim();
    const note = () => root.querySelector('.unchecked-note span')?.textContent?.trim();

    pick(component, 'discord:thread');
    fixture.detectChanges();
    expect(idLabel()).toBe('ADMIN.ID_DISCORD_THREAD');
    expect(note()).toBe('ADMIN.ID_UNCHECKED_NOTE');

    pick(component, 'webhook');
    fixture.detectChanges();
    expect(note()).toBe('ADMIN.ID_UNCHECKED_NOTE_URL');
  });

  /** Enter in a field submits the form only through a submit button the form owns. */
  it('makes Add the form’s submit button, disabled while the form is invalid', () => {
    const { component, fixture } = setup();
    const root = fixture.nativeElement as HTMLElement;
    const form = root.querySelector('form') as HTMLFormElement;
    const add = root.querySelector('mat-dialog-actions button[type="submit"]') as HTMLButtonElement;

    expect(form.id).toBeTruthy();
    expect(add).not.toBeNull();
    expect(add.getAttribute('form')).toBe(form.id);
    expect(add.disabled).toBe(true);

    pick(component, 'discord:channel');
    component.form.patchValue({ id: '1100000000000000001', name: 'raids' });
    fixture.detectChanges();
    expect(add.disabled).toBe(false);
  });

  it('saves on a submit of a valid form, and does nothing on an invalid one', () => {
    const { adminService, component, fixture } = setup();
    const form = (fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement;

    form.dispatchEvent(new Event('submit'));
    expect(adminService.createDestination).not.toHaveBeenCalled();

    pick(component, 'discord:channel');
    component.form.patchValue({ id: '1100000000000000001', name: 'raids' });
    form.dispatchEvent(new Event('submit'));
    expect(adminService.createDestination).toHaveBeenCalledTimes(1);
  });

  it('shows progress on Add and refuses a second submit while saving', () => {
    const pending = new Subject<void>();
    const { adminService, component, fixture } = setup(() => pending);
    pick(component, 'discord:channel');
    component.form.patchValue({ id: '1100000000000000001', name: 'raids' });

    component.submit();
    component.submit();
    fixture.detectChanges();

    expect(adminService.createDestination).toHaveBeenCalledTimes(1);
    const add = (fixture.nativeElement as HTMLElement).querySelector('mat-dialog-actions button[type="submit"]') as HTMLButtonElement;
    expect(add.disabled).toBe(true);
    expect(add.querySelector('mat-progress-spinner')).not.toBeNull();
  });

  /**
   * A refusal used to close the dialog and drop what the admin typed, with the reason in a snackbar over
   * the list. It now stays open and puts the reason on the field it is about.
   */
  describe('when the server refuses', () => {
    function refuse(error: unknown, kind = 'discord:channel', translations?: Record<string, unknown>) {
      const ctx = setup(() => throwError(() => error), translations);
      pick(ctx.component, kind);
      if (kind === 'telegram:topic') ctx.component.form.patchValue({ name: 'Topic', chatId: '-1001234567890', topicId: '42' });
      else ctx.component.form.patchValue({ id: '1100000000000000001', name: 'raids' });
      // The fields are on screen before anyone can press Add.
      ctx.fixture.detectChanges();
      ctx.component.submit();
      ctx.fixture.detectChanges();
      return ctx;
    }

    it('stays open with the input kept and the reason on the id field', () => {
      const { component, dialogRef, fixture } = refuse({ error: { error: 'Nope.' }, status: 400 });

      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(component.form.controls.id.value).toBe('1100000000000000001');
      expect(component.form.controls.name.value).toBe('raids');
      expect(component.form.controls.id.getError('server')).toBe('Nope.');
      expect((fixture.nativeElement as HTMLElement).querySelector('[data-test="id-field"] mat-error')?.textContent).toContain('Nope.');
    });

    it('puts the reason on the chat field for a topic', () => {
      const { component } = refuse({ error: { error: 'Nope.' }, status: 400 }, 'telegram:topic');

      expect(component.form.controls.chatId.getError('server')).toBe('Nope.');
    });

    it('clears the reason once the id is edited', () => {
      const { component } = refuse({ error: { error: 'Nope.' }, status: 400 });

      component.form.controls.id.setValue('1100000000000000002');

      expect(component.form.controls.id.hasError('server')).toBe(false);
      expect(component.form.valid).toBe(true);
    });

    it('shows the half-write warning the 502 carries', () => {
      const warning =
        'Poracle rejected the destination, and could not be asked whether part of it was saved. Check the destination list before trying again.';
      const { component } = refuse({ error: { error: warning }, status: 502 });

      expect(component.form.controls.id.getError('server')).toBe(warning);
    });

    it('keeps its own translated message for a server error that says nothing useful', () => {
      const { component } = refuse({ error: { error: 'An unexpected error occurred.' }, status: 500 });

      expect(component.form.controls.id.getError('server')).toBe('ADMIN.SNACK_FAILED_ADD_DESTINATION');
    });

    it('shows who already holds the id on a 409', () => {
      const { component } = refuse(
        { error: { name: 'raids', error: 'A destination with this id already exists.', type: 'discord:channel' }, status: 409 },
        'discord:channel',
        { ADMIN: { DESTINATION_EXISTS: 'Already in Poracle as "{{name}}" ({{type}}).', TYPE_DISCORD_CHANNEL: 'Discord channel' } },
      );

      expect(component.form.controls.id.getError('server')).toBe('Already in Poracle as "raids" (Discord channel).');
    });

    it.each([['discord:user'], ['telegram:user'], [null], ['']])('calls a %p holder a person’s account on a 409', type => {
      const { component } = refuse(
        { error: { name: 'Sam', error: 'A destination with this id already exists.', type }, status: 409 },
        'discord:channel',
        { ADMIN: { DESTINATION_EXISTS: 'Already in Poracle as "{{name}}" ({{type}}).', TYPE_PERSON: 'a person’s account' } },
      );

      expect(component.form.controls.id.getError('server')).toBe('Already in Poracle as "Sam" (a person’s account).');
    });

    it('still calls an unknown non-person type Other on a 409', () => {
      const { component } = refuse(
        { error: { name: 'Typo row', error: 'A destination with this id already exists.', type: 'x:unknown' }, status: 409 },
        'discord:channel',
        { ADMIN: { DESTINATION_EXISTS: 'Already in Poracle as "{{name}}" ({{type}}).', TYPE_OTHER: 'Other' } },
      );

      expect(component.form.controls.id.getError('server')).toBe('Already in Poracle as "Typo row" (Other).');
    });

    it('falls back to the server’s sentence on a 409 that could not name the holder', () => {
      const { component } = refuse({ error: { name: null, error: 'A destination with this id already exists.', type: null }, status: 409 });

      expect(component.form.controls.id.getError('server')).toBe('A destination with this id already exists.');
    });
  });
});
