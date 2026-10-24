import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PairDriftBannerComponent } from './pair-drift-banner.component';
import { DriftItem } from '../../../core/models';
import { AreaNameService } from '../../../core/services/area-name.service';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { LocationService } from '../../../core/services/location.service';
import { ToastService } from '../../../core/services/toast.service';

const location: DriftItem = {
  discord: { lat: 1.5, lon: 2.5 },
  field: 'location',
  fingerprint: 'fp-loc',
  telegram: { lat: 3.5, lon: 4.5 },
};
const areas: DriftItem = { discord: ['old town'], field: 'areas', fingerprint: 'fp-areas', telegram: ['old town', 'riverside'] };

describe('PairDriftBannerComponent', () => {
  let fixture: ComponentFixture<PairDriftBannerComponent>;
  let drift: ReturnType<typeof signal<DriftItem[]>>;
  let resolve: jest.Mock;
  let toast: { error: jest.Mock };
  let reverse: jest.Mock;
  let geocodingDisabled: boolean;

  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('.drift-item'));
  const settle = async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();
  };

  function mount(items: DriftItem[]): void {
    drift = signal(items);
    resolve = jest.fn().mockResolvedValue(undefined);
    toast = { error: jest.fn() };
    geocodingDisabled = false;
    reverse = jest.fn((lat: number) => of({ city: lat === 1.5 ? 'Old Town' : 'Northside', displayName: 'x' }));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: LinkedAccountService, useValue: { drift, resolve } },
        { provide: LocationService, useValue: { geocodingDisabled: () => geocodingDisabled, reverseGeocode: reverse } },
        { provide: AreaNameService, useValue: { label: (a: string) => a.replace(/\b\w/g, c => c.toUpperCase()) } },
        { provide: ToastService, useValue: toast },
      ],
      imports: [PairDriftBannerComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      DRIFT: {
        AREAS_EXTRA: '{{side}} also has {{names}}.',
        DISMISS: 'Dismiss',
        KEEP: 'Keep {{side}}',
        KEEP_ARIA: 'Keep {{side}}. {{line}}',
        LOCATION: 'Location: Discord has {{discord}}, Telegram has {{telegram}}.',
        RESOLVE_FAILED: 'Could not resolve that.',
        TITLE: 'Your linked accounts differ',
      },
      LINKED: { SIDE_DISCORD: 'Discord', SIDE_TELEGRAM: 'Telegram' },
    });
    translate.use('en');
    fixture = TestBed.createComponent(PairDriftBannerComponent);
    fixture.detectChanges();
  }

  beforeEach(() => sessionStorage.clear());

  it('renders nothing when there is no drift', () => {
    mount([]);
    expect(el().querySelector('.drift-banner')).toBeNull();
  });

  it('names both places for a location difference and offers two buttons', async () => {
    mount([location]);
    await settle();

    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('Old Town');
    expect(rows()[0].textContent).toContain('Northside');
    const keeps = Array.from(rows()[0].querySelectorAll('button')).map(b => b.textContent?.trim());
    expect(keeps).toEqual(['Keep Discord', 'Keep Telegram']);
  });

  it('falls back to coordinates when the lookup finds nothing', async () => {
    mount([location]);
    reverse.mockReturnValue(of(null));
    fixture.destroy();
    fixture = TestBed.createComponent(PairDriftBannerComponent);
    fixture.detectChanges();
    await settle();

    expect(rows()[0].textContent).toContain('1.5, 2.5');
  });

  it('asks nothing and shows coordinates when geocoding is switched off', async () => {
    mount([location]);
    geocodingDisabled = true;
    reverse.mockClear();
    fixture.destroy();
    fixture = TestBed.createComponent(PairDriftBannerComponent);
    fixture.detectChanges();
    await settle();

    expect(reverse).not.toHaveBeenCalled();
    expect(rows()[0].textContent).toContain('3.5, 4.5');
  });

  it('shows only the difference for areas', () => {
    mount([areas]);

    const text = rows()[0].textContent ?? '';
    expect(text).toContain('Telegram also has Riverside.');
    expect(text).not.toContain('Discord also has');
    expect(text).not.toContain('Old Town');
  });

  it('sends the choice with the item fingerprint', async () => {
    mount([areas]);
    (rows()[0].querySelectorAll('button')[1] as HTMLButtonElement).click();
    await settle();

    expect(resolve).toHaveBeenCalledWith('areas', 'telegram', 'fp-areas');
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('shows the server sentence once when a resolve is refused', async () => {
    mount([areas]);
    resolve.mockRejectedValue({ error: { error: 'That changed again. Check the banner.' }, status: 409 });
    (rows()[0].querySelectorAll('button')[0] as HTMLButtonElement).click();
    await settle();

    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('That changed again. Check the banner.');
  });

  it('uses its own sentence when the failure carries none', async () => {
    mount([areas]);
    resolve.mockRejectedValue({ status: 503 });
    (rows()[0].querySelectorAll('button')[0] as HTMLButtonElement).click();
    await settle();

    expect(toast.error).toHaveBeenCalledWith('Could not resolve that.');
  });

  it('hides on dismiss for the session, and comes back for a difference it has not seen', () => {
    mount([areas]);
    (el().querySelector('.drift-dismiss') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.drift-banner')).toBeNull();
    expect(sessionStorage.getItem('poracle-drift-dismissed')).toContain('fp-areas');

    drift.set([areas, location]);
    fixture.detectChanges();
    expect(el().querySelector('.drift-banner')).not.toBeNull();
    expect(rows()).toHaveLength(2);
  });

  it('stays hidden after a reload in the same session', () => {
    sessionStorage.setItem('poracle-drift-dismissed', JSON.stringify(['fp-areas']));
    mount([areas]);
    expect(el().querySelector('.drift-banner')).toBeNull();
  });

  it('still works when session storage throws', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    mount([areas]);
    expect(el().querySelector('.drift-banner')).not.toBeNull();
    (el().querySelector('.drift-dismiss') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.drift-banner')).toBeNull();
    jest.restoreAllMocks();
  });
});
