import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminWebhooksComponent } from './admin-webhooks.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';

function human(id: string, overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id,
    name: id,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    language: null,
    lastChecked: null,
    notes: null,
    type: 'discord:user',
    ...overrides,
  };
}

/**
 * Every global admin can manage every webhook. Listing them as delegates on each row put the same five
 * locked avatars on a hook nobody had been granted, and the real grants got lost among them.
 */
describe('AdminWebhooksComponent delegates column', () => {
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
      imports: [AdminWebhooksComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: { open: jest.fn() } });
    TestBed.overrideProvider(MatDialog, { useValue: { open: jest.fn() } });
    const fixture = TestBed.createComponent(AdminWebhooksComponent);
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

    expect(note?.textContent).toContain('ADMIN.WEBHOOKS_ADMINS_NOTE');
  });
});
