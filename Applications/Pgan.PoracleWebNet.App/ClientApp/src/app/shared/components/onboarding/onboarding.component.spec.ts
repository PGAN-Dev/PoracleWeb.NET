import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { OnboardingComponent } from './onboarding.component';
import { AreaService } from '../../../core/services/area.service';
import { DashboardService } from '../../../core/services/dashboard.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';

/**
 * The wizard offered its location and areas steps regardless of `disable_location` / `disable_areas`,
 * and read /api/areas either way -- a 403 and a feature-disabled toast on the dashboard of a site that
 * had switched areas off, and a wizard that could never read as complete. See #915's "left out".
 */
describe('OnboardingComponent', () => {
  const setup = (opts: { disabled?: string[]; hasAreas?: boolean; hasLocation?: boolean; hasAlarms?: boolean } = {}) => {
    const disabled = new Set(opts.disabled ?? []);
    const settings = signal<Record<string, string>>(Object.fromEntries([...disabled].map(k => [k, 'true'])));
    const areaService = { getSelected: jest.fn(() => of(opts.hasAreas ? ['downtown'] : [])) };
    const locationService = {
      getLocation: jest.fn(() => of(opts.hasLocation ? { latitude: 37.5, longitude: -77.4 } : { latitude: 0, longitude: 0 })),
      setLocation: jest.fn(),
    };
    const dashboardService = { getCounts: jest.fn(() => of({ raids: 0, pokemon: opts.hasAlarms ? 3 : 0 })) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        provideTranslateService(),
        { provide: AreaService, useValue: areaService },
        { provide: LocationService, useValue: locationService },
        { provide: DashboardService, useValue: dashboardService },
        { provide: MatDialog, useValue: { open: jest.fn() } },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => disabled.has(key), siteSettings: settings } },
      ],
    });

    const fixture = TestBed.createComponent(OnboardingComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const titles = () => [...el.querySelectorAll('.step h3')].map(h => h.textContent?.trim());
    const heading = () => el.querySelector('.onboarding-header h2')?.textContent?.trim();
    const activeTitle = () => el.querySelector('.step.active h3')?.textContent?.trim();
    return { activeTitle, areaService, el, fixture, heading, locationService, titles };
  };

  it('offers every step when nothing is disabled', () => {
    const { areaService, locationService, titles } = setup();

    expect(titles()).toEqual(['ONBOARDING.STEP_LOCATION_TITLE', 'ONBOARDING.STEP_AREAS_TITLE', 'ONBOARDING.STEP_ALARM_TITLE']);
    expect(areaService.getSelected).toHaveBeenCalled();
    expect(locationService.getLocation).toHaveBeenCalled();
  });

  it('leaves out the areas step, and does not read areas, under disable_areas', () => {
    const { areaService, titles } = setup({ disabled: ['disable_areas'] });

    expect(titles()).toEqual(['ONBOARDING.STEP_LOCATION_TITLE', 'ONBOARDING.STEP_ALARM_TITLE']);
    expect(areaService.getSelected).not.toHaveBeenCalled();
  });

  it('leaves out the location step, and does not read the location, under disable_location', () => {
    const { locationService, titles } = setup({ disabled: ['disable_location'] });

    expect(titles()).toEqual(['ONBOARDING.STEP_AREAS_TITLE', 'ONBOARDING.STEP_ALARM_TITLE']);
    expect(locationService.getLocation).not.toHaveBeenCalled();
  });

  it('reads as complete once every step it offers is done', () => {
    // With the areas step still counted behind the scenes, a site without areas could never finish.
    const { heading } = setup({ disabled: ['disable_areas'], hasAlarms: true, hasLocation: true });

    expect(heading()).toBe('ONBOARDING.TITLE_COMPLETE');
  });

  it('still waits for a step that is offered and not done', () => {
    const { heading } = setup({ hasAlarms: true, hasLocation: true });

    expect(heading()).toBe('ONBOARDING.TITLE_WELCOME');
  });

  it('opens on the first unfinished step it offers', () => {
    const { activeTitle } = setup({ disabled: ['disable_location'], hasAreas: true });

    expect(activeTitle()).toBe('ONBOARDING.STEP_ALARM_TITLE');
  });

  it('opens on the areas step when only the location is done', () => {
    const { activeTitle } = setup({ hasLocation: true });

    expect(activeTitle()).toBe('ONBOARDING.STEP_AREAS_TITLE');
  });

  it('sends the alarm step to an alarm type that is switched on', () => {
    const { el } = setup({ disabled: ['disable_mons'], hasAreas: true, hasLocation: true });
    const link = el.querySelector<HTMLAnchorElement>('.step.active a');

    expect(link?.getAttribute('href')).toBe('/raids');
  });
});
