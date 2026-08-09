import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';

import { TranslatedPaginatorIntl } from './translated-paginator-intl';

/**
 * Without a provided MatPaginatorIntl the paginator keeps Material's built-in English, so the admin
 * tables stayed partly English in every other locale. See #425.
 */
describe('TranslatedPaginatorIntl', () => {
  let intl: TranslatedPaginatorIntl;
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), TranslatedPaginatorIntl],
    });
    translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      PAGINATOR: {
        FIRST_PAGE: 'First page',
        ITEMS_PER_PAGE: 'Items per page:',
        LAST_PAGE: 'Last page',
        NEXT_PAGE: 'Next page',
        PREVIOUS_PAGE: 'Previous page',
        RANGE: '{{start}} - {{end}} of {{total}}',
        RANGE_EMPTY: '0 of {{total}}',
      },
    });
    translate.setTranslation('de', {
      PAGINATOR: {
        FIRST_PAGE: 'Erste Seite',
        ITEMS_PER_PAGE: 'Einträge pro Seite:',
        LAST_PAGE: 'Letzte Seite',
        NEXT_PAGE: 'Nächste Seite',
        PREVIOUS_PAGE: 'Vorherige Seite',
        RANGE: '{{start}} - {{end}} von {{total}}',
        RANGE_EMPTY: '0 von {{total}}',
      },
    });
    translate.use('en');
    intl = TestBed.inject(TranslatedPaginatorIntl);
  });

  it('translates the labels', () => {
    expect(intl.itemsPerPageLabel).toBe('Items per page:');
    expect(intl.nextPageLabel).toBe('Next page');
    expect(intl.firstPageLabel).toBe('First page');
  });

  it('builds a 1-based inclusive range', () => {
    expect(intl.getRangeLabel(0, 25, 120)).toBe('1 - 25 of 120');
    expect(intl.getRangeLabel(1, 25, 120)).toBe('26 - 50 of 120');
  });

  it('clamps the final page to the total rather than overshooting', () => {
    expect(intl.getRangeLabel(4, 25, 110)).toBe('101 - 110 of 110');
  });

  it('handles an empty table', () => {
    expect(intl.getRangeLabel(0, 25, 0)).toBe('0 of 0');
  });

  it('re-reads its labels when the language changes mid-session', () => {
    translate.use('de');

    expect(intl.itemsPerPageLabel).toBe('Einträge pro Seite:');
    expect(intl.getRangeLabel(0, 25, 120)).toBe('1 - 25 von 120');
  });
});
