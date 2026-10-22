import { Component, OnInit } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { ADD_ON_ARRIVAL, addOnArrival } from './add-on-arrival';

@Component({ selector: 'app-fake-list', template: '' })
class FakeListComponent implements OnInit {
  private readonly takeAddRequest = addOnArrival();
  opened = 0;

  ngOnInit(): void {
    if (this.takeAddRequest()) this.opened++;
  }
}

describe('addOnArrival', () => {
  beforeEach(() => TestBed.resetTestingModule());

  const setup = async (url: string) => {
    TestBed.configureTestingModule({ providers: [provideRouter([{ component: FakeListComponent, path: 'list' }])] });
    const harness = await RouterTestingHarness.create();
    const list = await harness.navigateByUrl(url, FakeListComponent);
    await harness.fixture.whenStable();
    return { list, router: TestBed.inject(Router) };
  };

  it('opens the add dialog once and takes the param out of the URL', async () => {
    const { list, router } = await setup('/list?add=1');

    expect(list.opened).toBe(1);
    expect(router.url).toBe('/list');
  });

  it('keeps other query params', async () => {
    const { router } = await setup('/list?add=1&tab=eggs');

    expect(router.url).toBe('/list?tab=eggs');
  });

  it('does nothing on a plain visit', async () => {
    const { list, router } = await setup('/list');

    expect(list.opened).toBe(0);
    expect(router.url).toBe('/list');
  });

  it('is what the dashboard sends', () => {
    expect(ADD_ON_ARRIVAL).toEqual({ add: 1 });
  });

  it('never opens without a router', () => {
    TestBed.configureTestingModule({});
    const take = TestBed.runInInjectionContext(() => addOnArrival());

    expect(take()).toBe(false);
  });
});
