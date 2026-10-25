import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';

import { PairDriftBannerComponent } from './pair-drift-banner.component';
import { DriftItem, LinkStatus } from '../../../core/models';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { DRIFT_REVIEW_STATE } from '../../utils/pair-drift';

const areas: DriftItem = { discord: ['old town'], field: 'areas', fingerprint: 'fp-areas', telegram: ['old town', 'riverside'] };
const location: DriftItem = { discord: { lat: 1, lon: 2 }, field: 'location', fingerprint: 'fp-loc', telegram: { lat: 3, lon: 4 } };

describe('PairDriftBannerComponent', () => {
  let fixture: ComponentFixture<PairDriftBannerComponent>;
  let drift: ReturnType<typeof signal<DriftItem[]>>;
  let status: ReturnType<typeof signal<LinkStatus | null>>;
  let events: Subject<unknown>;
  let url: string;

  const el = () => fixture.nativeElement as HTMLElement;
  const banner = () => el().querySelector('.drift-banner');
  const navigate = (to: string) => {
    url = to;
    events.next(new NavigationEnd(1, to, to));
    fixture.detectChanges();
  };

  /** Mounts with the status already read, as when the banner arrives with the first read. */
  function mount(items: DriftItem[], at = '/dashboard'): void {
    drift = signal(items);
    status = signal<LinkStatus | null>({ drift: items, linked: true });
    events = new Subject();
    url = at;
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTranslateService(), { provide: LinkedAccountService, useValue: { drift, status } }],
      imports: [PairDriftBannerComponent],
    });
    const router = TestBed.inject(Router);
    Object.defineProperty(router, 'events', { value: events });
    Object.defineProperty(router, 'url', { get: () => url });
    TestBed.inject(TranslateService).setTranslation('en', {
      DRIFT: {
        BANNER: 'Discord and Telegram have different settings.',
        DISMISS: 'Dismiss',
        REVIEW: 'Review',
        REVIEW_ARIA: 'Review the differences on Linked accounts',
        TITLE: 'Discord and Telegram have different settings',
      },
    });
    TestBed.inject(TranslateService).use('en');
    fixture = TestBed.createComponent(PairDriftBannerComponent);
    fixture.detectChanges();
  }

  beforeEach(() => sessionStorage.clear());

  it('renders nothing when there is no drift', () => {
    mount([]);
    expect(banner()).toBeNull();
  });

  it('is one line that points at Linked accounts, however many differences there are', () => {
    mount([areas, location]);

    expect(banner()).not.toBeNull();
    expect(el().querySelectorAll('.drift-banner p')).toHaveLength(1);
    expect(el().querySelector('.drift-text')?.textContent?.trim()).toBe('Discord and Telegram have different settings.');
    const review = el().querySelector('a.drift-review') as HTMLAnchorElement;
    expect(review.getAttribute('href')).toBe('/linked-accounts');
    expect(review.getAttribute('aria-label')).toBe('Review the differences on Linked accounts');
    // No per-difference buttons: those live on Linked accounts.
    expect(el().querySelectorAll('button')).toHaveLength(1);
  });

  it('is not shown on Linked accounts, which lists the differences itself', () => {
    mount([areas], '/linked-accounts');
    expect(banner()).toBeNull();

    navigate('/dashboard');
    expect(banner()).not.toBeNull();
  });

  it('waits for a navigation before appearing for drift found mid-page, so nothing moves under the reader', () => {
    mount([]);
    drift.set([areas]);
    fixture.detectChanges();
    expect(banner()).toBeNull();

    navigate('/pokemon');
    expect(banner()).not.toBeNull();
  });

  it('shows at once for drift found by the first read after sign-in', () => {
    mount([]);
    status.set(null);
    fixture.detectChanges();

    drift.set([areas]);
    status.set({ drift: [areas], linked: true });
    fixture.detectChanges();
    expect(banner()).not.toBeNull();
  });

  it('goes away at once when the differences are settled', () => {
    mount([areas]);
    drift.set([]);
    fixture.detectChanges();
    expect(banner()).toBeNull();
  });

  it('hides on dismiss for the session, moves focus to the page heading, and comes back for a difference it has not seen', () => {
    const main = document.createElement('main');
    main.id = 'main-content';
    main.tabIndex = -1;
    const heading = document.createElement('h1');
    main.appendChild(heading);
    document.body.appendChild(main);
    mount([areas]);

    (el().querySelector('.drift-dismiss') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(banner()).toBeNull();
    // The page's own heading, not the whole main region, so the next Tab goes on through the page.
    expect(document.activeElement).toBe(heading);
    expect(heading.getAttribute('tabindex')).toBe('-1');
    expect(sessionStorage.getItem('poracle-drift-dismissed')).toContain('fp-areas');

    drift.set([areas, location]);
    navigate('/raids');
    expect(banner()).not.toBeNull();
    main.remove();
  });

  it('falls back to the main region on a page with no heading', () => {
    const main = document.createElement('main');
    main.id = 'main-content';
    main.tabIndex = -1;
    document.body.appendChild(main);
    mount([areas]);

    (el().querySelector('.drift-dismiss') as HTMLButtonElement).click();
    expect(document.activeElement).toBe(main);
    main.remove();
  });

  it('tells Linked accounts it was opened to review, so the page can take focus', () => {
    mount([areas]);
    const router = TestBed.inject(Router);
    const spy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    (el().querySelector('a.drift-review') as HTMLAnchorElement).click();

    expect(spy).toHaveBeenCalled();
    const extras = spy.mock.calls[0][1] as { state?: Record<string, unknown> } | undefined;
    expect(extras?.state?.[DRIFT_REVIEW_STATE]).toBe(true);
  });

  it('stays hidden after a reload in the same session', () => {
    sessionStorage.setItem('poracle-drift-dismissed', JSON.stringify(['fp-areas']));
    mount([areas]);
    expect(banner()).toBeNull();
  });
});
