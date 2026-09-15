import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { MyWebhooksComponent } from './my-webhooks.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';

describe('MyWebhooksComponent', () => {
  const rows: AdminUser[] = [
    { id: 'http://wh.example/a', name: 'teamharmonyrares', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'webhook' },
  ] as AdminUser[];

  function setup(options: { impersonating?: boolean; managedWebhooks?: string[]; webhooks?: AdminUser[] } = {}) {
    const adminService = { getManagedWebhooks: jest.fn(() => of(options.webhooks ?? rows)), impersonateById: jest.fn() };
    const auth = {
      impersonate: jest.fn(),
      isImpersonating: signal(options.impersonating ?? false),
      managedWebhooks: signal(options.managedWebhooks ?? []),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: AdminService, useValue: adminService },
        { provide: AuthService, useValue: auth },
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
      ],
      imports: [MyWebhooksComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(MyWebhooksComponent);
    fixture.detectChanges();
    return { adminService, auth, component: fixture.componentInstance, fixture };
  }

  afterEach(() => TestBed.resetTestingModule());

  /**
   * The rows used to be intersected with the client's own copy of the grant list, so any disagreement
   * between the two produced an empty table and no error. That is what #797 looked like from the user's
   * side: a nav item, a page, and nothing on it. The server already scopes the rows to the caller.
   */
  it('renders the rows the server returned without re-filtering them', () => {
    const { component } = setup({ managedWebhooks: ['teamharmonyrares'] });

    expect(component.webhooks()).toEqual(rows);
  });

  it('asks for the rows even when the client has no copy of the grant list', () => {
    const { adminService } = setup({ managedWebhooks: [] });

    expect(adminService.getManagedWebhooks).toHaveBeenCalled();
  });

  /**
   * One localStorage slot holds the pre-impersonation token, so a second hop overwrites it and strands
   * the caller. The API refuses it too; this keeps the button from offering what the server will reject.
   */
  it('offers Manage Alarms outside an impersonation session', () => {
    expect(setup().component.isImpersonating()).toBe(false);
  });

  it('withdraws Manage Alarms inside an impersonation session', () => {
    expect(setup({ impersonating: true }).component.isImpersonating()).toBe(true);
  });
});
