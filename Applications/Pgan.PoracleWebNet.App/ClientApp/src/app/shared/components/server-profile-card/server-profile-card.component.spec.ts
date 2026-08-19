import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { ServerProfileCardComponent } from './server-profile-card.component';
import { PoracleServerProfile } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';

/**
 * The card exists so "this server is too old for the feature you just used" is visible somewhere other
 * than the logs. These cover the states it has to tell apart: fine, old, unreachable, and unknown.
 */
describe('ServerProfileCardComponent', () => {
  let adminService: { getServerProfile: jest.Mock };

  const upToDate = { latest: null, running: null, state: 'UpToDate' as const };

  const base: PoracleServerProfile = {
    belowMinimum: false,
    capabilities: { autocreate: true, buttons: true, snapshots: false },
    checkedAt: '2026-08-19T19:00:00Z',
    minimumSupported: '5.1.0',
    poracleUpdate: upToDate,
    reachable: true,
    schemaVersion: 5,
    version: '5.1.0',
    webUpdate: upToDate,
  };

  function setup(profile: PoracleServerProfile | null) {
    adminService = {
      getServerProfile: jest.fn().mockReturnValue(profile ? of(profile) : throwError(() => new Error('down'))),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AdminService, useValue: adminService },
      ],
      imports: [ServerProfileCardComponent],
    });

    const fixture = TestBed.createComponent(ServerProfileCardComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('lists only the capabilities that are on, in order', () => {
    // A key reported false means the binary knows the feature and has it switched off; listing it
    // beside the live ones would read as support.
    const component = setup(base).componentInstance;

    expect(component.enabledCapabilities()).toEqual(['autocreate', 'buttons']);
  });

  it('shows the version and schema once loaded', () => {
    const fixture = setup(base);

    expect(fixture.componentInstance.serverLoading()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('5.1.0');
    expect(fixture.nativeElement.textContent).toContain('5');
  });

  it('warns when the server is older than this build needs', () => {
    const fixture = setup({ ...base, belowMinimum: true, version: '5.0.4' });

    expect(fixture.nativeElement.querySelector('.server-alert-error')).not.toBeNull();
  });

  it('says so when the server did not answer', () => {
    const fixture = setup({ ...base, capabilities: {}, reachable: false, version: null });

    expect(fixture.nativeElement.querySelector('.server-alert-warn')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.server-alert-error')).toBeNull();
  });

  it('does not warn about age on a healthy server', () => {
    // The legitimate twin: a banner that shows on a good server is a banner people stop reading.
    const fixture = setup(base);

    expect(fixture.nativeElement.querySelector('.server-alert')).toBeNull();
  });

  it('stops loading rather than spinning forever when the call fails', () => {
    const fixture = setup(null);

    expect(fixture.componentInstance.serverLoading()).toBe(false);
    expect(fixture.componentInstance.serverProfile()).toBeNull();
  });

  it('says when a component is behind its latest release', () => {
    const fixture = setup({
      ...base,
      poracleUpdate: { latest: '5.2.0', running: '5.1.0', state: 'Behind' },
    });

    expect(fixture.nativeElement.querySelector('.server-alert-update')).not.toBeNull();
  });

  it('names a development build rather than calling it out of date', () => {
    // Running ahead of every release is how a develop build identifies itself; "update available"
    // would be exactly backwards.
    const fixture = setup({
      ...base,
      poracleUpdate: { latest: '5.1.0', running: '5.2.0', state: 'PreRelease' },
    });

    expect(fixture.nativeElement.querySelector('.server-alert-info')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.server-alert-update')).toBeNull();
  });

  it('shows no update line when both are current', () => {
    const fixture = setup(base);

    expect(fixture.nativeElement.querySelector('.server-alert-update')).toBeNull();
    expect(fixture.nativeElement.querySelector('.server-alert-info')).toBeNull();
  });

  it('re-probes when asked, instead of answering from the cache', () => {
    const component = setup(base).componentInstance;
    component.refreshServerProfile();

    expect(adminService.getServerProfile).toHaveBeenNthCalledWith(1, false);
    expect(adminService.getServerProfile).toHaveBeenNthCalledWith(2, true);
  });
});
