import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { MasterDataService } from './masterdata.service';

const API = 'http://test-api';

/**
 * Grunt display names taken from the server rather than from eleven locale files. See #840.
 *
 * The fixture is the real shape of `GET /api/masterdata/grunts` on a PoracleNG carrying #217, trimmed
 * to the cases that behave differently: a gendered pair, a gender-fixed pair, a leader with a ticketed
 * and an unticketed entry at the same gender, and three grunts this UI has never been able to name.
 */
const GRUNTS = {
  // A typed grunt: one entry per gender, no gender-0 entry at all.
  '10': { name: 'Dark - Grunt (Female)', short_name: 'Dark ♀', gender: 2, grunt_type: 'dark', type: 'Dark' },
  '11': { name: 'Dark - Grunt (Male)', short_name: 'Dark ♂', gender: 1, grunt_type: 'dark', type: 'Dark' },
  // Two entries at the SAME gender — ticketed and unticketed.
  '44': { name: 'Giovanni', short_name: 'Giovanni', gender: 0, grunt_type: 'giovanni', type: 'Giovanni' },
  '45': { name: 'Giovanni Unticketed', short_name: 'Giovanni Unticketed', gender: 0, grunt_type: 'giovanni', type: 'Giovanni' },
  // Never nameable before: no GRUNT_DISPLAY_KEYS entry exists for any of these.
  '52': { name: 'DieCurryWurst', short_name: 'DieCurryWurst', gender: 0, grunt_type: 'npc_3', type: 'Npc_3' },
  '53': { name: 'Blanche', short_name: 'Blanche', gender: 0, grunt_type: 'blanche', type: 'Blanche' },
  '54': { name: 'Jessie', short_name: 'Jessie', gender: 2, grunt_type: 'gruntb', type: 'Gruntb' },
  '55': { name: 'James', short_name: 'James', gender: 1, grunt_type: 'gruntb', type: 'Gruntb' },
};

describe('MasterDataService — grunt names', () => {
  let httpMock: HttpTestingController;
  let service: MasterDataService;

  function load(grunts: unknown): void {
    service.loadData().subscribe();
    httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/costumes`).flush({});
    httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});

    const request = httpMock.expectOne(req => req.url === `${API}/api/masterdata/grunts`);
    if (grunts === null) {
      request.error(new ProgressEvent('error'), { status: 404, statusText: 'Not Found' });
    } else {
      request.flush(grunts);
    }
  }

  beforeEach(() => {
    // MasterDataService is root-provided and caches, so each case needs a fresh injector.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: I18nService, useValue: { currentLang: () => 'en', instant: (key: string) => key } },
      ],
    });
    service = TestBed.inject(MasterDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('asks for the display language, so a language change refetches the names', () => {
    service.loadData().subscribe();
    const request = httpMock.expectOne(req => req.url === `${API}/api/masterdata/grunts`);

    expect(request.request.params.get('locale')).toBe('en');

    request.flush({});
    httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/items`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/moves`).flush({});
    httpMock.expectOne(`${API}/api/masterdata/costumes`).flush({});
    httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});
  });

  it('names each gender of a typed grunt', () => {
    load(GRUNTS);

    expect(service.getGruntName('dark', 1)).toBe('Dark ♂');
    expect(service.getGruntName('dark', 2)).toBe('Dark ♀');
  });

  it('names grunts this UI has never had a string for', () => {
    // The gain, not the deletion. None of these have a GRUNT_DISPLAY_KEYS entry, so all three render
    // as "Unknown grunt" today — including any rule created from the bot.
    load(GRUNTS);

    expect(service.getGruntName('npc_3', 0)).toBe('DieCurryWurst');
    expect(service.getGruntName('blanche', 0)).toBe('Blanche');
    expect(service.getGruntName('gruntb', 1)).toBe('James');
    expect(service.getGruntName('gruntb', 2)).toBe('Jessie');
  });

  it('declines to name a typed grunt set to any gender', () => {
    // `dark` has a male entry and a female entry and no gender-0 one. Answering either would tell the
    // user a rule that matches both is narrower than it is, so this falls through to the label the UI
    // already has.
    load(GRUNTS);

    expect(service.getGruntName('dark', 0)).toBeNull();
    expect(service.getGruntName('dark', undefined)).toBeNull();
  });

  it('declines when one grunt type and gender matches two entries', () => {
    // Giovanni is ticketed and unticketed at gender 0. Picking the first is picking arbitrarily.
    load(GRUNTS);

    expect(service.getGruntName('giovanni', 0)).toBeNull();
  });

  it('names nothing at all on a Poracle that does not serve them', () => {
    // Every released PoracleNG. The route answers, but its entries carry no grunt_type or short_name,
    // so nothing is indexed and every label stays exactly where it is today.
    load({
      '10': { gender: 2, grunt: 'Grunt', type: 'Dark' },
      '11': { gender: 1, grunt: 'Grunt', type: 'Dark' },
    });

    expect(service.getGruntName('dark', 1)).toBeNull();
    expect(service.getGruntName('dark', 2)).toBeNull();
  });

  it('survives the request failing outright', () => {
    load(null);

    expect(service.getGruntName('dark', 1)).toBeNull();
    expect(service.isLoaded()).toBe(true);
  });

  it('answers null for no grunt type rather than throwing', () => {
    load(GRUNTS);

    expect(service.getGruntName(null, 0)).toBeNull();
    expect(service.getGruntName('', 0)).toBeNull();
  });
});
