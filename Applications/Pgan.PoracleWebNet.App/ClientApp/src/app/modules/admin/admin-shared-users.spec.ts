import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';

import { AdminDestinationsComponent } from './admin-destinations.component';
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

/**
 * Admin > Users and Admin > Destinations filter one list of about 2,700 rows. Each page waited for its own
 * fetch on every visit; now the second page shows the first page's rows at once and checks them in the
 * background. A row change and Refresh fetch again, and a change of account drops the list. See #1038.
 */
describe('Admin Users and Destinations share one list', () => {
  const sam = row('sam');
  const hook = row('https://hook/a', { name: 'Hook A', kind: 'destination', type: 'webhook' });
  let user: WritableSignal<{ id: string; isAdmin: boolean } | null>;

  function setup() {
    user = signal({ id: 'admin1', isAdmin: true });
    const adminService = {
      deleteUser: jest.fn(() => of(undefined)),
      disableUser: jest.fn(() => of({ ...sam, adminDisable: 1 })),
      enableUser: jest.fn(() => of(sam)),
      getAllWebhookDelegates: jest.fn(() => of({})),
      getPoracleAdmins: jest.fn(() => of([])),
      getPorocleDelegates: jest.fn(() => of({})),
      getUsers: jest.fn(() => of([sam, hook])),
      pauseUser: jest.fn(() => of({ ...hook, enabled: 0 })),
      resumeUser: jest.fn(() => of(hook)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: AuthService, useValue: { impersonate: jest.fn(), isImpersonating: signal(false), user } },
      ],
      imports: [AdminUsersComponent, AdminDestinationsComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    TestBed.overrideProvider(MatDialog, { useValue: { open: jest.fn(() => ({ afterClosed: () => of(true) })) } });
    return adminService;
  }

  beforeAll(() => {
    (globalThis as unknown as { IntersectionObserver: unknown }).IntersectionObserver = class {
      disconnect(): void {}
      observe(): void {}
    };
  });

  afterEach(() => TestBed.resetTestingModule());

  it('shows Destinations its rows from the Users fetch without waiting for its own', () => {
    const adminService = setup();

    const users = TestBed.createComponent(AdminUsersComponent);
    users.detectChanges();
    users.destroy();
    adminService.getUsers.mockReturnValue(new Subject<AdminUser[]>());
    const destinations = TestBed.createComponent(AdminDestinationsComponent);
    destinations.detectChanges();

    expect(destinations.componentInstance.usersLoading()).toBe(false);
    expect(destinations.componentInstance.list.filtered().map(u => u.id)).toEqual([hook.id]);
    expect(adminService.getUsers).toHaveBeenCalledTimes(2);
  });

  it('refetches once after a row changes, and the other page sees the change', () => {
    const adminService = setup();
    const destinations = TestBed.createComponent(AdminDestinationsComponent);
    destinations.detectChanges();

    destinations.componentInstance.pauseWebhook(hook);
    expect(adminService.getUsers).toHaveBeenCalledTimes(2);

    adminService.getUsers.mockReturnValue(of([{ ...sam, adminDisable: 1 }, hook]));
    const users = TestBed.createComponent(AdminUsersComponent);
    users.detectChanges();
    const beforeBlock = adminService.getUsers.mock.calls.length;
    users.componentInstance.toggleUser(sam, false);

    expect(adminService.getUsers).toHaveBeenCalledTimes(beforeBlock + 1);
    expect(users.componentInstance.filteredUsers()[0].adminDisable).toBe(1);
  });

  it('refetches a deleted row away on both pages', () => {
    const adminService = setup();
    const users = TestBed.createComponent(AdminUsersComponent);
    users.detectChanges();

    adminService.getUsers.mockReturnValue(of([hook]));
    users.componentInstance.deleteUser(sam);

    expect(adminService.getUsers).toHaveBeenCalledTimes(2);
    expect(users.componentInstance.filteredUsers()).toEqual([]);
  });

  it('refetches when Refresh is pressed', () => {
    const adminService = setup();
    const users = TestBed.createComponent(AdminUsersComponent);
    users.detectChanges();

    const refresh = Array.from((users.nativeElement as HTMLElement).querySelectorAll('button')).find(b =>
      b.textContent?.includes('ADMIN.REFRESH'),
    )!;
    refresh.click();

    expect(adminService.getUsers).toHaveBeenCalledTimes(2);
  });

  it('fetches afresh for the next account after sign-out', () => {
    const adminService = setup();
    const users = TestBed.createComponent(AdminUsersComponent);
    users.detectChanges();
    users.destroy();

    user.set(null);
    user.set({ id: 'admin2', isAdmin: true });
    const again = TestBed.createComponent(AdminUsersComponent);
    again.detectChanges();

    expect(adminService.getUsers).toHaveBeenCalledTimes(2);
  });
});
