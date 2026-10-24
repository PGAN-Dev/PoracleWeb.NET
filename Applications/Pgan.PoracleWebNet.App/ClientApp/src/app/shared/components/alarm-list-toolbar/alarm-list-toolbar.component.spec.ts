import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { AlarmListToolbarComponent } from './alarm-list-toolbar.component';

describe('AlarmListToolbarComponent delivery filter', () => {
  let fixture: ComponentFixture<AlarmListToolbarComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), provideNoopAnimations()],
      imports: [AlarmListToolbarComponent],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ALARM: {
        ALARMS_COUNT_OTHER: '{{count}} alarms',
        CLEAR_FILTERS: 'Clear search and filters',
        FILTER_ALL: 'All',
        FILTER_DELIVERED_TO: 'Show alarms delivered to',
        FILTER_DELIVERED_TO_LABEL: 'Delivered to',
        NO_MATCHES: 'No alarms match.',
        NO_MATCHES_DESTINATION: '{{alarms}}: none are delivered to {{app}}.',
        RESULT_COUNT_OTHER: '{{filtered}} of {{total}} alarms',
        SORT_BY: 'Sort by',
      },
      DELIVER_TO: { DISCORD: 'Discord', LABEL: 'Which app gets this alarm?', TELEGRAM: 'Telegram' },
      INVASIONS: { PAGE_TITLE: 'Invasion alarms' },
    });
    translate.use('en');
    fixture = TestBed.createComponent(AlarmListToolbarComponent);
    fixture.componentRef.setInput('searchPlaceholder', 'X');
    fixture.componentRef.setInput('total', 3);
    fixture.componentRef.setInput('shown', 3);
  });

  it('has no filter for a person with no link', () => {
    fixture.detectChanges();

    expect(el().querySelector('.destination-filter')).toBeNull();
  });

  it('offers All, Discord and Telegram when linked, and writes the choice back', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.detectChanges();

    const buttons = Array.from(el().querySelectorAll<HTMLButtonElement>('.destination-filter button'));
    expect(buttons.map(b => b.textContent?.trim())).toEqual(['All', 'Discord', 'Telegram']);

    buttons[2].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.destinationFilter()).toBe('telegram');
  });

  it('goes back to All when the filter stops being offered (the pair was unlinked)', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.componentInstance.destinationFilter.set('telegram');
    fixture.detectChanges();

    fixture.componentRef.setInput('canFilterDestination', false);
    fixture.detectChanges();

    expect(fixture.componentInstance.destinationFilter()).toBe('all');
  });

  it('Clear filters puts the delivery filter back on All', () => {
    fixture.componentRef.setInput('canFilterDestination', true);
    fixture.componentInstance.destinationFilter.set('discord');

    fixture.componentInstance.clear();

    expect(fixture.componentInstance.destinationFilter()).toBe('all');
  });

  describe('looks and reads as a filter, not one more feature chip', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('canFilterDestination', true);
      fixture.detectChanges();
    });

    it('carries a visible "Delivered to" label', () => {
      expect(el().querySelector('.destination-filter-label')?.textContent?.trim()).toBe('Delivered to');
    });

    it('is a segmented control named for what it filters, not "Deliver to"', () => {
      const group = el().querySelector('.destination-filter mat-button-toggle-group') as HTMLElement;

      expect(group).not.toBeNull();
      expect(group.getAttribute('aria-label')).toBe('Show alarms delivered to');
      expect(el().querySelectorAll('.destination-filter mat-button-toggle')).toHaveLength(3);
    });

    it('draws each app with its glyph beside its name', () => {
      const glyphs = Array.from(el().querySelectorAll('.destination-filter app-app-glyph')).map(g => g.getAttribute('data-app'));

      expect(glyphs).toEqual(['discord', 'telegram']);
    });

    it('marks the chosen option pressed', () => {
      fixture.componentInstance.destinationFilter.set('discord');
      fixture.detectChanges();

      const pressed = Array.from(el().querySelectorAll<HTMLButtonElement>('.destination-filter button')).map(b =>
        b.getAttribute('aria-checked'),
      );
      expect(pressed).toEqual(['false', 'true', 'false']);
    });
  });

  describe('the empty state when only the app filter hides everything', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('canFilterDestination', true);
      fixture.componentRef.setInput('alarmsLabel', 'INVASIONS.PAGE_TITLE');
      fixture.componentRef.setInput('shown', 0);
    });

    it('says which alarms are not delivered to which app', () => {
      fixture.componentInstance.destinationFilter.set('telegram');
      fixture.detectChanges();

      expect(el().querySelector('.no-matches p')?.textContent?.trim()).toBe('Invasion alarms: none are delivered to Telegram.');
    });

    it('keeps the generic line when a search is in play too', () => {
      fixture.componentInstance.destinationFilter.set('telegram');
      fixture.componentInstance.search.set('abc');
      fixture.detectChanges();

      expect(el().querySelector('.no-matches p')?.textContent?.trim()).toBe('No alarms match.');
    });

    it('keeps the generic line when the list has filters of its own on', () => {
      fixture.componentRef.setInput('hasFilters', true);
      fixture.componentInstance.destinationFilter.set('telegram');
      fixture.detectChanges();

      expect(el().querySelector('.no-matches p')?.textContent?.trim()).toBe('No alarms match.');
    });
  });
});

/** Shape 2: every list names its alarms, so the app-filter empty state never falls back to the generic line. */
describe('every alarm list names its alarms for the toolbar', () => {
  it.each([
    ['pokemon/pokemon', 'POKEMON'],
    ['raids/raid', 'RAIDS'],
    ['quests/quest', 'QUESTS'],
    ['invasions/invasion', 'INVASIONS'],
    ['pokestop-events/pokestop-event', 'POKESTOP_EVENTS'],
    ['lures/lure', 'LURES'],
    ['nests/nest', 'NESTS'],
    ['gyms/gym', 'GYMS'],
    ['max-battles/max-battle', 'MAX_BATTLES'],
    ['fort-changes/fort-change', 'FORT_CHANGES'],
  ])('%s', (list, section) => {
    const html = readFileSync(join(__dirname, '../../../modules', `${list}-list.component.html`), 'utf8');

    expect(html).toContain(`alarmsLabel="${section}.PAGE_TITLE"`);
  });
});
