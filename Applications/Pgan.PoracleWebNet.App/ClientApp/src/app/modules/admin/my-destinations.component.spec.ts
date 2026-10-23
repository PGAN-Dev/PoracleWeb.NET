import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
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

  it('says how to get access when there are no destinations', () => {
    const { fixture } = setup({ destinations: [] });
    expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
  });

  it('explains the managed session instead of the empty state, with a way back', () => {
    const { auth, fixture } = setup({ destinations: [], impersonating: true });
    (auth as unknown as { stopImpersonating: jest.Mock }).stopImpersonating = jest.fn();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2')).toBeNull();
    expect(el.querySelector('[data-test="managing-note"]')).not.toBeNull();
    (el.querySelector('[data-test="exit-managing"]') as HTMLButtonElement).click();
    expect((auth as unknown as { stopImpersonating: jest.Mock }).stopImpersonating).toHaveBeenCalled();
  });

  it('marks the ID column so it can drop away on a phone', () => {
    const { fixture } = setup();
    expect(fixture.nativeElement.querySelector('td.col-id')).not.toBeNull();
  });

  // The ID cell's tooltip carried the whole webhook URL, token and all. Destinations already hid it.
  it('never shows a webhook token, on screen or in a tooltip', () => {
    const token = 'SecretTokenAbc123xyz';
    const hook = { ...rows[0], id: `https://discord.com/api/webhooks/1234567890/${token}` } as AdminUser;
    const channel = mixed[1];
    const { fixture } = setup({ destinations: [hook, channel] });

    expect((fixture.nativeElement as HTMLElement).innerHTML).not.toContain(token);
    const tooltips = fixture.debugElement.queryAll(By.directive(MatTooltip)).map(d => d.injector.get(MatTooltip).message);
    expect(tooltips.some(m => m.includes(token))).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('discord.com/api/webhooks/1234567890…');
    // A channel id is no secret, and stays whole.
    expect(tooltips).toContain(channel.id);
  });

  /**
   * At 390px the table needed 402px of a 358px column: Manage alarms ran off the screen and names broke
   * every few letters. A phone gets one item per destination instead, the same data stacked.
   */
  it('lists each destination as a phone-sized item with its kind, status and its own Manage alarms', () => {
    const { fixture } = setup({ destinations: mixed.slice(0, 2) });
    const items = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[data-test="destination-item"]'));

    expect(items).toHaveLength(2);
    expect(items[0].querySelector('.item-name')?.textContent?.trim()).toBe('teamharmonyrares');
    expect(items[0].querySelector('.kind-label')?.textContent?.trim()).toBe('ADMIN.TYPE_WEBHOOK');
    expect(items[0].querySelector('.status-chip')).not.toBeNull();
    expect(items[1].querySelector('.kind-label')?.textContent?.trim()).toBe('ADMIN.TYPE_DISCORD_CHANNEL');
    expect(items[1].querySelector('button.manage-btn')).not.toBeNull();
  });

  /**
   * The list loaded while managing belongs to the managed destination, which has none. Going back used
   * to leave "No destinations assigned" on screen until a reload, telling a delegate he had nothing.
   */
  it('reloads the caller’s own destinations when the managed session ends', async () => {
    const { adminService, auth, fixture } = setup({ destinations: [], impersonating: true });
    adminService.getManagedDestinations.mockReturnValue(of(mixed));
    (auth as unknown as { stopImpersonating: jest.Mock }).stopImpersonating = jest.fn(async () => {
      auth.isImpersonating.set(false);
      return true;
    });

    ((fixture.nativeElement as HTMLElement).querySelector('[data-test="exit-managing"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(adminService.getManagedDestinations).toHaveBeenCalledTimes(2);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('[data-test="destination-item"]')).toHaveLength(4);
    expect((fixture.nativeElement as HTMLElement).querySelector('.empty-state')).toBeNull();
  });

  it('reloads when the session ends from the banner too', () => {
    const { adminService, auth, fixture } = setup({ destinations: [], impersonating: true });
    adminService.getManagedDestinations.mockReturnValue(of(mixed));

    auth.isImpersonating.set(false);
    fixture.detectChanges();

    expect(adminService.getManagedDestinations).toHaveBeenCalledTimes(2);
  });

  it('does not reload while nothing changes', () => {
    const { adminService, fixture } = setup();
    fixture.detectChanges();

    expect(adminService.getManagedDestinations).toHaveBeenCalledTimes(1);
  });
});
