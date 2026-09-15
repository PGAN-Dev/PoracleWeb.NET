import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { ConfigService } from './config.service';
import { MasterDataService } from './masterdata.service';

/**
 * Masterdata almost never wins the race against the first render: every alarm list paints its cards
 * from the alarm rows, which come back first, and resolves each species name through
 * `MasterDataService`. If the maps are not reactive, that first paint is also the last one and the
 * cards keep the `Pokemon #1` fallback until something else happens to redraw them -- which is what
 * a route change does, and why the names look right on the second visit.
 *
 * Every test here flushes the responses *after* the first read, because seeding the service first
 * passes just as happily against the broken code.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-masterdata-host',
  standalone: true,
  template: '<h3>{{ masterData.getPokemonName(1) }}</h3>',
})
class MasterDataHostComponent {
  readonly masterData = inject(MasterDataService);
}

describe('MasterDataService — data arriving after first render', () => {
  const API = 'http://test-api';
  let httpMock: HttpTestingController;
  let service: MasterDataService;

  function flushMasterData(): void {
    httpMock.expectOne(`${API}/api/masterdata/pokemon`).flush({ '1': 'Bulbasaur' });
    httpMock.expectOne(`${API}/api/masterdata/items`).flush({ '1': 'Poke Ball' });
    httpMock.expectOne(`${API}/api/masterdata/moves`).flush({ '13': 'Wrap' });
    httpMock.expectOne(`${API}/api/masterdata/costumes`).flush({ '85': 'Halloween 2025' });
    httpMock.expectOne(req => req.url === `${API}/api/masterdata/monsters`).flush({});
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(MasterDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('repaints a rendered species name once the names land', () => {
    const fixture = TestBed.createComponent(MasterDataHostComponent);
    service.loadData().subscribe();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h3').textContent).toBe('Pokemon #1');

    flushMasterData();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h3').textContent).toBe('Bulbasaur');
  });

  it('keeps the fallback when the names never land', () => {
    const fixture = TestBed.createComponent(MasterDataHostComponent);
    service.loadData().subscribe();
    fixture.detectChanges();

    httpMock.expectOne(`${API}/api/masterdata/pokemon`).error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    httpMock.match(`${API}/api/masterdata/items`);
    httpMock.match(`${API}/api/masterdata/moves`);
    httpMock.match(`${API}/api/masterdata/costumes`);
    httpMock.match(req => req.url === `${API}/api/masterdata/monsters`);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h3').textContent).toBe('Pokemon #1');
  });

  it('re-resolves item and move names read before the load finished', () => {
    const item = TestBed.runInInjectionContext(() => service.getItemName(1));
    const move = TestBed.runInInjectionContext(() => service.getMoveName(13));
    expect(item).toBe('Item #1');
    expect(move).toBe('Move #13');

    service.loadData().subscribe();
    flushMasterData();

    expect(service.getItemName(1)).toBe('Poke Ball');
    expect(service.getMoveName(13)).toBe('Wrap');
  });
});
