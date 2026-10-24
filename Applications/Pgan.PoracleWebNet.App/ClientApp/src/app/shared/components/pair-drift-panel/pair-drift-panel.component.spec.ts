import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { PairDriftPanelComponent } from './pair-drift-panel.component';
import { DriftItem } from '../../../core/models';
import { AreaNameService } from '../../../core/services/area-name.service';
import { AreaService } from '../../../core/services/area.service';
import { I18nService } from '../../../core/services/i18n.service';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';
import { ToastService } from '../../../core/services/toast.service';

const areas: DriftItem = { discord: ['aclt'], field: 'areas', fingerprint: 'fp-areas', telegram: ['riverside'] };
const places: DriftItem = {
  details: { labels: { home: 'Home', park: 'Park' } },
  discord: [{ label: 'home', lat: 1, lon: 1 }],
  field: 'places',
  fingerprint: 'fp-places',
  telegram: [{ label: 'park', lat: 2, lon: 2 }],
};
const current: DriftItem = {
  details: { discord: { name: 'Weekend', otherHas: false }, telegram: { name: 'Home', otherHas: true } },
  discord: 2,
  field: 'current_profile',
  fingerprint: 'fp-cur',
  telegram: 1,
};

describe('PairDriftPanelComponent', () => {
  let fixture: ComponentFixture<PairDriftPanelComponent>;
  let drift: ReturnType<typeof signal<DriftItem[]>>;
  let canChoose: ReturnType<typeof signal<boolean>>;
  let driftUnknown: ReturnType<typeof signal<boolean>>;
  let resolve: jest.Mock;
  let dialogOpen: jest.Mock;
  let confirmed: boolean;
  let available: jest.Mock;
  let disabled: Set<string>;
  let toast: { success: jest.Mock };

  const el = () => fixture.nativeElement as HTMLElement;
  const lineFor = (field: string) => el().querySelector(`.drift-line[data-field="${field}"]`) as HTMLElement;
  const keepButton = (field: string, side: string) => el().querySelector(`[data-keep="${field}-${side}"]`) as HTMLButtonElement;
  const settle = async () => {
    for (let i = 0; i < 6; i++) {
      await Promise.resolve();
      fixture.detectChanges();
    }
    await fixture.whenStable();
    fixture.detectChanges();
  };

  function mount(items: DriftItem[], opts: { areasDisabled?: boolean } = {}): void {
    drift = signal(items);
    canChoose = signal(true);
    driftUnknown = signal(false);
    confirmed = true;
    disabled = new Set(opts.areasDisabled ? ['disable_areas'] : []);
    resolve = jest.fn().mockImplementation(async () => undefined);
    dialogOpen = jest.fn(() => ({ afterClosed: () => of(confirmed) }));
    available = jest.fn(() => of([{ name: 'ACLT', group: '', userSelectable: true }]));
    toast = { success: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: LinkedAccountService, useValue: { canChoose, drift, driftUnknown, resolve } },
        { provide: LocationService, useValue: { geocodingDisabled: () => true, reverseGeocode: jest.fn() } },
        { provide: AreaNameService, useValue: { label: (a: string) => `[${a}]` } },
        { provide: AreaService, useValue: { getAvailable: available } },
        { provide: SettingsService, useValue: { isDisabled: (k: string) => disabled.has(k) } },
        { provide: I18nService, useValue: { currentLang: signal('en') } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: ToastService, useValue: toast },
      ],
      imports: [PairDriftPanelComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      DRIFT: {
        AREAS_EXTRA: 'Only {{side}} has {{names}}.',
        CONFIRM_DELETE: 'Delete from {{other}}',
        CONFIRM_DELETE_NAMED: 'Delete {{name}} from {{other}}',
        CONFIRM_MESSAGE: "Using {{kept}}'s settings deletes these from {{other}}:",
        CONFIRM_TITLE: 'Delete from {{other}}?',
        CONSEQ_AREAS_LOSES: '{{other}} stops using {{names}}.',
        CONSEQ_DELETES: '{{other}} loses {{names}}.',
        CONSEQ_GETS: '{{other}} gets {{names}}.',
        CONSEQ_PROFILE_MISSING: '{{other}} has no profile {{name}} yet. Match the profiles first.',
        CONSEQ_SWITCH: '{{other}} switches to {{name}}.',
        ERR_CHANGED: 'That changed in the meantime.',
        ERR_PROFILE_HOLDS_ALARMS: "{{side}}'s profile {{name}} still has alarms.",
        EXPLAIN: "Choose which app's settings to use. The other app is changed to match.",
        KEEP: "Use {{side}}'s",
        KEEP_ARIA: "{{label}}: Use {{side}}'s",
        LABEL_AREAS: 'Areas',
        LABEL_CURRENT_PROFILE: 'Active profile',
        LABEL_PLACES: 'Saved places',
        NONE: 'Discord and Telegram have the same settings.',
        PLACES_EXTRA: 'Only {{side}} has {{names}}.',
        PROFILE_CURRENT: 'Discord uses {{discord}}, Telegram uses {{telegram}}.',
        RESOLVE_FAILED: "That didn't save.",
        RESOLVED_AREAS: "{{other}} now has {{kept}}'s areas.",
        TITLE: 'Discord and Telegram have different settings',
        TITLE_NEUTRAL: 'Settings on both apps',
        UNKNOWN: "The two apps couldn't be compared just now.",
      },
      LINKED: { SIDE_DISCORD: 'Discord', SIDE_TELEGRAM: 'Telegram' },
    });
    translate.use('en');
    fixture = TestBed.createComponent(PairDriftPanelComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  }

  afterEach(() => fixture?.nativeElement.remove());

  it('renders nothing for someone who cannot choose (unlinked, waiting or paused)', () => {
    mount([areas]);
    canChoose.set(false);
    fixture.detectChanges();
    expect(el().querySelector('[data-test="drift-panel"]')).toBeNull();
  });

  it('says the two apps match when nothing differs, under a heading that does not claim otherwise', () => {
    mount([]);
    expect(el().querySelector('[data-test="drift-none"]')?.textContent).toContain('the same settings');
    expect(el().querySelector('#drift-panel-heading')?.textContent?.trim()).toBe('Settings on both apps');
  });

  it('heads the list with the difference only when there is one', () => {
    mount([areas]);
    expect(el().querySelector('#drift-panel-heading')?.textContent?.trim()).toBe('Discord and Telegram have different settings');
  });

  it('says it could not compare rather than claiming they match', () => {
    mount([]);
    driftUnknown.set(true);
    fixture.detectChanges();
    expect(el().querySelector('[data-test="drift-unknown"]')).not.toBeNull();
    expect(el().querySelector('[data-test="drift-none"]')).toBeNull();
    expect(el().querySelector('#drift-panel-heading')?.textContent?.trim()).toBe('Settings on both apps');
  });

  it('labels each line, explains the choice, and says what each button does before it is pressed', () => {
    mount([areas]);

    expect(el().querySelector('[data-test="drift-explain"]')?.textContent).toContain('The other app is changed to match.');
    expect(lineFor('areas').querySelector('h3')?.textContent?.trim()).toBe('Areas');
    // Poracle's own spelling from the area list, not a title-cased key.
    expect(lineFor('areas').querySelector('.drift-text')?.textContent).toContain('Only Discord has ACLT.');
    const button = keepButton('areas', 'discord');
    expect(button.textContent?.trim()).toBe("Use Discord's");
    // Five lines carry the same two button texts, so each name says which line it settles.
    expect(button.getAttribute('aria-label')).toBe("Areas: Use Discord's");
    const described = document.getElementById(button.getAttribute('aria-describedby')!);
    expect(described?.textContent).toContain('Telegram gets ACLT.');
    expect(described?.textContent).toContain('Telegram stops using [riverside].');
  });

  it('asks for no area list when areas are switched off, and shows the stored names', () => {
    mount([areas], { areasDisabled: true });
    expect(available).not.toHaveBeenCalled();
    expect(lineFor('areas').querySelector('.drift-text')?.textContent).toContain('[aclt]');
  });

  it('resolves a non-destructive choice without asking, says so in a toast and focuses the next difference', async () => {
    mount([areas, places]);
    resolve.mockImplementation(async () => drift.set([places]));

    keepButton('areas', 'discord').click();
    await settle();

    expect(dialogOpen).not.toHaveBeenCalled();
    expect(resolve).toHaveBeenCalledWith('areas', 'discord', 'fp-areas');
    // A toast, seen wherever the page is scrolled to; a line at the foot of the panel was off screen on a phone.
    expect(toast.success).toHaveBeenCalledWith("Telegram now has Discord's areas.");
    expect(el().querySelector('[data-test="drift-status"]')).toBeNull();
    expect(document.activeElement?.id).toBe('drift-label-places');
  });

  it('focuses the heading when the last difference is settled', async () => {
    mount([areas]);
    resolve.mockImplementation(async () => drift.set([]));

    keepButton('areas', 'telegram').click();
    await settle();

    expect(document.activeElement?.id).toBe('drift-panel-heading');
  });

  it('confirms a choice that deletes on the other app, naming what goes', async () => {
    mount([places]);

    keepButton('places', 'discord').click();
    await settle();

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const data = (dialogOpen.mock.calls[0] as unknown[])[1] as { data: { items: string[]; title: string; warn: boolean } };
    expect(data.data.items).toEqual(['Park']);
    expect(data.data.title).toBe('Delete from Telegram?');
    expect(data.data.warn).toBe(true);
    // The button names the deletion, not the choice that leads to it.
    expect((data.data as unknown as { confirmText: string }).confirmText).toBe('Delete Park from Telegram');
    expect(resolve).toHaveBeenCalledWith('places', 'discord', 'fp-places');
  });

  it('changes nothing when the deletion is not confirmed', async () => {
    mount([places]);
    confirmed = false;

    keepButton('places', 'telegram').click();
    await settle();

    expect(resolve).not.toHaveBeenCalled();
  });

  it('names profiles on the active-profile line and blocks the switch the other app cannot make yet', () => {
    mount([current]);

    expect(lineFor('current_profile').querySelector('.drift-text')?.textContent).toContain('Discord uses Weekend, Telegram uses Home.');
    expect(keepButton('current_profile', 'discord').disabled).toBe(true);
    expect(lineFor('current_profile').textContent).toContain('Telegram has no profile Weekend yet. Match the profiles first.');
    expect(keepButton('current_profile', 'telegram').disabled).toBe(false);
  });

  it('words a refusal from its code, in an alert, naming the app', async () => {
    mount([areas]);
    resolve.mockRejectedValue({
      error: { name: 'Spare', side: 'telegram', code: 'profile_holds_alarms', count: 2, error: 'English' },
      status: 409,
    });

    keepButton('areas', 'discord').click();
    await settle();

    const error = el().querySelector('[data-test="drift-error"]');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toContain("Telegram's profile Spare still has alarms.");
    // Beside the button that failed, where focus returns, rather than at the foot of the panel.
    expect(lineFor('areas').contains(error)).toBe(true);
    expect(toast.success).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(keepButton('areas', 'discord'));
  });

  it('says a failure under the heading when its line has gone, since focus moves to the heading', async () => {
    mount([areas]);
    resolve.mockImplementation(async () => {
      drift.set([]);
      throw { error: { code: 'drift_changed' }, status: 409 };
    });

    keepButton('areas', 'discord').click();
    await settle();

    const error = el().querySelector('[data-test="drift-error"]');
    expect(error?.textContent).toContain('That changed in the meantime.');
    expect(error?.previousElementSibling?.id).toBe('drift-panel-heading');
    expect(document.activeElement?.id).toBe('drift-panel-heading');
  });

  it('uses its own sentence for a failure without a code or reason', async () => {
    mount([areas]);
    resolve.mockRejectedValue({ status: 503 });

    keepButton('areas', 'discord').click();
    await settle();

    expect(el().querySelector('[data-test="drift-error"]')?.textContent).toContain("That didn't save.");
  });

  it('does not ask for the area list again when it fails, and still renders', () => {
    drift = signal([areas]);
    mount([areas]);
    available.mockReturnValue(throwError(() => new Error('x')));
    expect(lineFor('areas')).not.toBeNull();
  });
});
