import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AddWebhookDialogComponent, WebhookDelegatesDialogComponent } from './admin-webhooks.component';
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
    language: null,
    lastChecked: null,
    notes: null,
    type,
  };
}

describe('WebhookDelegatesDialogComponent', () => {
  const webhook = user('https://example.com/hook', 'Hook', 'webhook');
  const sam = user('222', 'Sam');

  function setup() {
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
        { provide: MAT_DIALOG_DATA, useValue: { allUsers: [webhook, sam], poracleAdmins: [], porocleDelegates: [], webhook } },
      ],
      imports: [WebhookDelegatesDialogComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });

    const fixture = TestBed.createComponent(WebhookDelegatesDialogComponent);
    fixture.detectChanges();
    return { adminService, component: fixture.componentInstance, fixture, snackBar };
  }

  afterEach(() => TestBed.resetTestingModule());

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

describe('AddWebhookDialogComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MatDialogRef, useValue: { close: jest.fn() } }],
      imports: [AddWebhookDialogComponent, NoopAnimationsModule],
    });
    return TestBed.createComponent(AddWebhookDialogComponent).componentInstance;
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
