import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { AdminComponent } from './admin.component';
import { PoracleServerProfile } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';

/**
 * The card exists so that "this server is too old for the feature you just used" is visible somewhere
 * other than the logs. These cover the three states it has to tell apart: fine, old, and unknown.
 */
describe('AdminComponent server profile', () => {
  let adminService: { getServerProfile: jest.Mock };

  const base: PoracleServerProfile = {
    belowMinimum: false,
    capabilities: { autocreate: true, buttons: true, snapshots: false },
    checkedAt: '2026-08-19T19:00:00Z',
    minimumSupported: '5.1.0',
    reachable: true,
    schemaVersion: 5,
    version: '5.1.0',
  };

  function setup(profile: PoracleServerProfile | null) {
    adminService = {
      getServerProfile: jest.fn().mockReturnValue(profile ? of(profile) : throwError(() => new Error('down'))),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AdminService, useValue: adminService },
      ],
      imports: [AdminComponent],
    });

    const fixture = TestBed.createComponent(AdminComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('lists only the capabilities that are on, in order', () => {
    // A key reported false means the binary knows the feature and has it switched off; listing it
    // beside the live ones would read as support.
    const component = setup(base);

    expect(component.enabledCapabilities()).toEqual(['autocreate', 'buttons']);
  });

  it('shows the profile once it loads', () => {
    const component = setup(base);

    expect(component.loading()).toBe(false);
    expect(component.profile()?.version).toBe('5.1.0');
  });

  it('keeps the card empty rather than spinning forever when the call fails', () => {
    const component = setup(null);

    expect(component.loading()).toBe(false);
    expect(component.profile()).toBeNull();
  });

  it('re-probes when asked, instead of answering from the cache', () => {
    const component = setup(base);
    component.refresh();

    expect(adminService.getServerProfile).toHaveBeenNthCalledWith(1, false);
    expect(adminService.getServerProfile).toHaveBeenNthCalledWith(2, true);
  });

  it('has nothing to list when the server reports no capabilities', () => {
    const component = setup({ ...base, capabilities: {} });

    expect(component.enabledCapabilities()).toEqual([]);
  });
});
