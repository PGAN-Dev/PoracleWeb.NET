import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AdminUsersComponent } from './admin-users.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';

function row(id: string, overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id,
    name: id,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    kind: 'person',
    language: null,
    lastChecked: null,
    notes: null,
    type: 'discord:user',
    ...overrides,
  };
}

describe('AdminUsersComponent', () => {
  const me = row('admin1', { name: 'Jordan' });
  const otherAdmin = row('admin2', { name: 'Other Admin' });
  const member = row('u2', { name: 'Sam', currentProfileNo: 0 });

  function setup(confirm = true) {
    const adminService = {
      deleteUser: jest.fn(() => of(undefined)),
      deleteUserAlarms: jest.fn(() => of({ deleted: 0 })),
      disableUser: jest.fn(() => of({ ...member, adminDisable: 1 })),
      enableUser: jest.fn(() => of(member)),
      getUsers: jest.fn(() => of([me, otherAdmin, member])),
    };
    const snackBar = { open: jest.fn() };
    const dialog = { open: jest.fn(() => ({ afterClosed: () => of(confirm) })) };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: AuthService, useValue: { impersonate: jest.fn(), user: signal({ id: 'admin1', isAdmin: true }) } },
      ],
      imports: [AdminUsersComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });

    const fixture = TestBed.createComponent(AdminUsersComponent);
    fixture.detectChanges();
    return { adminService, component: fixture.componentInstance, dialog, fixture, snackBar };
  }

  beforeAll(() => {
    // The avatar cell waits for visibility before loading; jsdom has no IntersectionObserver.
    (globalThis as unknown as { IntersectionObserver: unknown }).IntersectionObserver = class {
      disconnect(): void {}
      observe(): void {}
    };
  });

  afterEach(() => TestBed.resetTestingModule());

  /** Opens the row's menu and reads what it offers, by visible label. */
  function actionLabels(fixture: ReturnType<typeof setup>['fixture'], rowIndex: number): string[] {
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row');
    (rows[rowIndex].querySelector('[data-test="row-menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const labels = Array.from(document.querySelectorAll('.mat-mdc-menu-panel [mat-menu-item]')).map(
      b => b.querySelector('.mat-mdc-menu-item-text')?.textContent?.trim() ?? '',
    );
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
    return labels;
  }

  /** The server refuses both for the caller's own id (#613 for block); offering them just produced an error. */
  it('offers neither Block nor Delete on the signed-in admin’s own row', () => {
    const { fixture } = setup();

    const labels = actionLabels(fixture, 0);
    expect(labels).not.toContain('ADMIN.BLOCK_USER');
    expect(labels).not.toContain('ADMIN.DELETE_USER');
    expect(labels).toContain('ADMIN.DELETE_ALL_ALARMS');
  });

  it('still offers Block and Delete on another admin and on a member', () => {
    const { fixture } = setup();

    for (const index of [1, 2]) {
      const labels = actionLabels(fixture, index);
      expect(labels).toContain('ADMIN.BLOCK_USER');
      expect(labels).toContain('ADMIN.DELETE_USER');
    }
  });

  it('keeps View on the row and names every other action in the row menu', () => {
    const { fixture } = setup();

    const row = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row')[2];
    const inline = Array.from(row.querySelectorAll('.action-buttons > button')).map(b => b.getAttribute('data-test'));
    expect(inline).toEqual(['view-as', 'row-menu']);
    expect(row.querySelector('[data-test="row-menu"]')?.getAttribute('aria-label')).toBe('ADMIN.MORE_ACTIONS_FOR');

    expect(actionLabels(fixture, 2)).toEqual(['ADMIN.STOP_ALERTS', 'ADMIN.BLOCK_USER', 'ADMIN.DELETE_ALL_ALARMS', 'ADMIN.DELETE_USER']);
  });

  it('offers Start alerts and Unblock where they apply', () => {
    const { adminService, fixture } = setup();
    adminService.getUsers.mockReturnValue(of([me, row('u3', { adminDisable: 1, enabled: 0 })]));
    fixture.componentInstance.loadUsers();
    fixture.detectChanges();

    expect(actionLabels(fixture, 1)).toEqual(['ADMIN.START_ALERTS', 'ADMIN.UNBLOCK_USER', 'ADMIN.DELETE_ALL_ALARMS', 'ADMIN.DELETE_USER']);
  });

  it('views as the user straight from the row', () => {
    const { component, fixture } = setup();
    const spy = jest.spyOn(component, 'impersonate').mockImplementation(() => undefined);

    const row = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row')[2];
    (row.querySelector('[data-test="view-as"]') as HTMLButtonElement).click();

    expect(spy).toHaveBeenCalledWith(member);
  });

  // A legacy OIDC or Telegram row can carry no type at all, and it is still a person.
  it('lists people only, including a null type', () => {
    const { adminService, component, fixture } = setup();
    adminService.getUsers.mockReturnValue(
      of([
        me,
        row('legacy', { type: null }),
        row('https://hook/a', { kind: 'destination', type: 'webhook' }),
        row('-1001234567890', { kind: 'destination', type: 'telegram:group' }),
        row('999', { kind: 'destination', type: 'x:unknown' }),
      ]),
    );
    component.loadUsers();
    fixture.detectChanges();

    expect(component.filteredUsers().map(u => u.id)).toEqual(['admin1', 'legacy']);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row')).toHaveLength(2);
  });

  it('points at Destinations from its description', () => {
    const { fixture } = setup();

    const link = (fixture.nativeElement as HTMLElement).querySelector('[data-test="destinations-link"]');
    expect(link?.getAttribute('href')).toBe('/admin/destinations');
    expect(link?.textContent).toContain('ADMIN.USERS_DESTINATIONS_LINK');
    // Phones hide .page-description, so the link must not live inside it.
    expect(link?.closest('.page-description')).toBeNull();
  });

  it('shows profile 0 as 0, not 1', () => {
    const { fixture } = setup();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row');
    const profileCell = rows[2].querySelector('.mat-column-profileNo');
    expect(profileCell?.textContent?.trim()).toBe('0');
  });

  it('shows the server’s reason when a block is refused', () => {
    const { adminService, component, snackBar } = setup();
    adminService.disableUser.mockReturnValue(throwError(() => ({ error: { error: 'You cannot block your own account.' }, status: 400 })));

    component.toggleUser(member, false);

    expect(snackBar.open).toHaveBeenCalledWith('You cannot block your own account.', expect.anything(), expect.anything());
  });

  it('falls back to the generic message when a block fails without a reason', () => {
    const { adminService, component, snackBar } = setup();
    adminService.disableUser.mockReturnValue(throwError(() => ({ status: 0 })));

    component.toggleUser(member, false);

    expect(snackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_BLOCK', expect.anything(), expect.anything());
  });

  it('shows the server’s reason when a delete is refused', () => {
    const { adminService, component, snackBar } = setup();
    adminService.deleteUser.mockReturnValue(throwError(() => ({ error: { error: 'You cannot delete your own account.' }, status: 400 })));

    component.deleteUser(member);

    expect(snackBar.open).toHaveBeenCalledWith('You cannot delete your own account.', expect.anything(), expect.anything());
  });

  it('asks before deleting with the destructive confirm and the message that names what goes', () => {
    const { adminService, component, dialog } = setup();

    component.deleteUser(member);

    const data = (dialog.open.mock.calls[0] as unknown[])[1] as { data: { message: string; warn: boolean } };
    expect(data.data.warn).toBe(true);
    expect(data.data.message).toBe('ADMIN.CONFIRM_DELETE_USER');
    expect(adminService.deleteUser).toHaveBeenCalledWith('u2');
  });
});
