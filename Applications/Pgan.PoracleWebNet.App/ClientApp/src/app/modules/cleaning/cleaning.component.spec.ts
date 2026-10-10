import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { CleaningComponent } from './cleaning.component';
import { DashboardCounts } from '../../core/models';
import { CleaningService } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { SettingsService } from '../../core/services/settings.service';

const NO_ALARMS: DashboardCounts = {
  raids: 0,
  eggs: 0,
  fortChanges: 0,
  gyms: 0,
  invasions: 0,
  lures: 0,
  maxBattles: 0,
  nests: 0,
  pokemon: 0,
  pokestopEvents: 0,
  quests: 0,
};

const ALL_OFF = {
  raids: false,
  eggs: false,
  gyms: false,
  invasions: false,
  lures: false,
  maxbattles: false,
  monsters: false,
  nests: false,
  quests: false,
};

/**
 * The API reports a type with no alarms as not clean -- there is nothing to be clean -- and "Enable
 * All" only flipped to "Disable All" once every row read on. So a user with Pokemon and raid alarms,
 * both cleaned, was told to enable cleaning forever. A type with no alarms is not applicable, and
 * neither the header button nor its own toggle should count it.
 */
describe('CleaningComponent', () => {
  const setup = (counts: Partial<DashboardCounts>, status: Partial<typeof ALL_OFF>, disabled: string[] = []) => {
    const settings = signal<Record<string, string>>(Object.fromEntries(disabled.map(k => [k, 'true'])));
    const cleaning = {
      getStatus: jest.fn(() => of({ ...ALL_OFF, ...status })),
      toggleAll: jest.fn(() => of({ skipped: [], updated: 3 })),
      toggleClean: jest.fn(() => of({ updated: 1 })),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        { provide: CleaningService, useValue: cleaning },
        { provide: DashboardService, useValue: { getCounts: () => of({ ...NO_ALARMS, ...counts }) } },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => settings()[key] === 'true', siteSettings: settings } },
      ],
    });

    const fixture = TestBed.createComponent(CleaningComponent);
    fixture.detectChanges();
    const row = (type: string) => fixture.componentInstance.cleaningItems.find(i => i.type === type)!;
    return { cleaning, component: fixture.componentInstance, fixture, row };
  };

  it('offers Disable All once every type that has alarms is cleaned', () => {
    const { component } = setup({ raids: 2, pokemon: 4 }, { raids: true, monsters: true });

    expect(component.allEnabled()).toBe(true);
  });

  it('offers Enable All while a type with alarms is not cleaned', () => {
    const { component } = setup({ raids: 2, pokemon: 4 }, { raids: false, monsters: true });

    expect(component.allEnabled()).toBe(false);
  });

  it('offers Enable All when there are no alarms at all', () => {
    const { component } = setup({}, {});

    expect(component.allEnabled()).toBe(false);
    expect(component.hasApplicable()).toBe(false);
  });

  /** A disabled type is not on the page, so it cannot hold the button back either. */
  it('ignores a disabled type that still has uncleaned alarms', () => {
    const { component } = setup({ pokemon: 4, quests: 1 }, { monsters: true, quests: false }, ['disable_quests']);

    expect(component.allEnabled()).toBe(true);
  });

  it('flips to Disable All after Enable All, without marking empty types on', () => {
    const { component, row } = setup({ pokemon: 4 }, {});

    component.toggleAll(true);

    expect(component.allEnabled()).toBe(true);
    expect(row('monsters').enabled()).toBe(true);
    expect(row('lures').enabled()).toBe(false);
  });

  it('shows each row with alarms as the API reports it, and disables the toggle on an empty one', () => {
    const { fixture, row } = setup({ raids: 2, pokemon: 4 }, { raids: false, monsters: true });
    const toggles = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('.cleaning-row mat-slide-toggle button'),
    ] as HTMLButtonElement[];

    expect(row('monsters').enabled()).toBe(true);
    expect(row('raids').enabled()).toBe(false);
    // Rows render in cleaningItems order: monsters, raids, eggs, ...
    expect(toggles[0].disabled).toBe(false);
    expect(toggles[1].disabled).toBe(false);
    expect(toggles[2].disabled).toBe(true);
  });
});

describe('CleaningComponent with nothing to clean', () => {
  const setup = (counts: Partial<DashboardCounts>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: MatSnackBar, useValue: { open: jest.fn() } },
        {
          provide: CleaningService,
          useValue: { getStatus: () => of(ALL_OFF), toggleAll: jest.fn(() => of({ skipped: [], updated: 0 })), toggleClean: jest.fn() },
        },
        { provide: DashboardService, useValue: { getCounts: () => of({ ...NO_ALARMS, ...counts }) } },
        { provide: SettingsService, useValue: { isDisabled: () => false, siteSettings: signal({}) } },
      ],
    });
    const fixture = TestBed.createComponent(CleaningComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { banner: el.querySelector('.recommendation-banner'), button: el.querySelector<HTMLButtonElement>('.header-actions button')! };
  };

  it('does not offer Enable All to a user with no alarms', () => {
    // Clicking it reported "Cleaning enabled for all types (0 alarms updated)" and nothing changed.
    const { banner, button } = setup({});

    expect(button.disabled).toBe(true);
    expect(banner).toBeNull();
  });

  it('still offers Enable All, with the recommendation, once a type has alarms', () => {
    const { banner, button } = setup({ quests: 1 });

    expect(button.disabled).toBe(false);
    expect(banner).not.toBeNull();
  });
});
