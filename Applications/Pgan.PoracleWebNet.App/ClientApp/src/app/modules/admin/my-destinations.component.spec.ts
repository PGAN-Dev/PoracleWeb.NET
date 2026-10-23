import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { MyDestinationsComponent } from './my-destinations.component';
import { AdminUser } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';

describe('MyDestinationsComponent', () => {
  const rows: AdminUser[] = [
    { id: 'http://wh.example/a', name: 'teamharmonyrares', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'webhook' },
  ] as AdminUser[];

  const mixed: AdminUser[] = [
    { id: 'http://wh.example/a', name: 'teamharmonyrares', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'webhook' },
    { id: '1100000000000000001', name: 'raids-north', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'discord:channel' },
    { id: '-1001234567890', name: 'hundos', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'telegram:group' },
    { id: '-1009876543210', name: 'typo', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'telegram:chanel' },
  ] as AdminUser[];

  function setup(options: { destinations?: AdminUser[]; impersonating?: boolean; managedDestinations?: string[] } = {}) {
    const adminService = { getManagedDestinations: jest.fn(() => of(options.destinations ?? rows)), impersonateById: jest.fn() };
    const auth = {
      impersonate: jest.fn(),
      isImpersonating: signal(options.impersonating ?? false),
      managedDestinations: signal(options.managedDestinations ?? []),
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
      imports: [MyDestinationsComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(MyDestinationsComponent);
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
    const { component } = setup({ managedDestinations: ['teamharmonyrares'] });

    expect(component.destinations()).toEqual(rows);
  });

  it('lists each delegated destination with its kind', () => {
    const { fixture } = setup({ destinations: mixed });
    const cells = Array.from(fixture.nativeElement.querySelectorAll('td.mat-column-type') as NodeListOf<HTMLElement>).map(td => ({
      icon: td.querySelector('mat-icon')?.textContent?.trim(),
      label: td.querySelector('.kind-label')?.textContent?.trim(),
    }));

    expect(cells).toEqual([
      { icon: 'webhook', label: 'ADMIN.TYPE_WEBHOOK' },
      { icon: 'tag', label: 'ADMIN.TYPE_DISCORD_CHANNEL' },
      { icon: 'send', label: 'ADMIN.TYPE_TELEGRAM_GROUP' },
      // An unknown type shows its raw string, as it does on Destinations, so a typo stays visible.
      { icon: 'help_outline', label: 'telegram:chanel' },
    ]);
  });

  it('records My Destinations as the way back out of the session', () => {
    // A delegate has no /admin; the session's exit has to come back here.
    const { adminService, auth, component } = setup();
    adminService.impersonateById.mockReturnValue(of({ token: 'webhook-jwt' }));

    component.manageAlarms(rows[0]);

    expect(auth.impersonate).toHaveBeenCalledWith('webhook-jwt', '/my-destinations');
  });

  it('asks for the rows even when the client has no copy of the grant list', () => {
    const { adminService } = setup({ managedDestinations: [] });

    expect(adminService.getManagedDestinations).toHaveBeenCalled();
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
