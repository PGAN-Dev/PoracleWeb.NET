import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { BULK_COUNT_REANNOUNCE_MS, BULK_COUNT_SETTLE_MS, BulkActionBarComponent } from './bulk-action-bar.component';

describe('BulkActionBarComponent', () => {
  let fixture: ComponentFixture<BulkActionBarComponent>;

  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.bulk-toolbar button'));
  const byText = (text: string) => buttons().find(b => b.textContent?.includes(text))!;
  const deleteButton = () => el().querySelector<HTMLButtonElement>('.bulk-delete')!;

  const render = (count: number, anySelected = count > 0) => {
    fixture.componentRef.setInput('count', count);
    fixture.componentRef.setInput('anySelected', anySelected);
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideTranslateService()], imports: [BulkActionBarComponent] });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ALARM: {
        DELETE: 'Delete',
        DELETE_SELECTED: 'Delete {{count}}',
        DELETE_SELECTED_LABEL_ONE: 'Delete {{count}} alarm',
        DELETE_SELECTED_LABEL_OTHER: 'Delete {{count}} alarms',
        DESELECT_ALL: 'Deselect all',
        SELECT_ALL: 'Select all',
        SELECTED_COUNT: '{{count}} selected',
        UPDATE_DISTANCE: 'Change radius',
      },
    });
    translate.use('en');
    fixture = TestBed.createComponent(BulkActionBarComponent);
  });

  it('names the count on the Delete button, so it cannot be mistaken for Change radius', () => {
    render(4);

    expect(el().querySelector('.bulk-count')?.textContent?.trim()).toBe('4 selected');
    expect(deleteButton().textContent?.trim()).toContain('Delete 4');
    expect(deleteButton().getAttribute('aria-label')).toBe('Delete 4 alarms');
  });

  it('draws Delete as the filled destructive action and Change radius as the tonal one', () => {
    render(2);

    expect(deleteButton().classList).toContain('mat-warn');
    expect(deleteButton().classList).toContain('mat-mdc-unelevated-button');
    const radius = byText('Change radius');
    expect(radius.classList).not.toContain('mat-warn');
    expect(radius.classList).toContain('mat-tonal-button');
  });

  it('puts Select all and Deselect all as text buttons, and Delete last', () => {
    render(1);

    const select = byText('Select all');
    const deselect = byText('Deselect all');
    for (const b of [select, deselect]) {
      expect(b.classList).toContain('mat-mdc-button');
      expect(b.classList).not.toContain('mat-warn');
    }
    expect(buttons()[buttons().length - 1]).toBe(deleteButton());
  });

  it('turns both actions off with nothing on screen selected, and keeps Deselect while hidden ticks remain', () => {
    render(0, true);

    expect(deleteButton().disabled).toBe(true);
    expect(byText('Change radius').disabled).toBe(true);
    expect(deleteButton().textContent?.trim()).toContain('Delete');
    expect(deleteButton().textContent).not.toContain('0');
    expect(byText('Deselect all')).toBeTruthy();
  });

  it('offers no Deselect all when nothing is selected', () => {
    render(0, false);

    expect(buttons().some(b => b.textContent?.includes('Deselect all'))).toBe(false);
  });

  describe('announcing the count', () => {
    const live = () => el().querySelector('[role="status"]');

    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('keeps the visible count out of the live region, so each tick is not read aloud', () => {
      render(1);

      expect(el().querySelector('.bulk-count')?.getAttribute('role')).toBeNull();
      expect(el().querySelector('.bulk-count')?.getAttribute('aria-live')).toBeNull();
    });

    it('announces the count once it settles, not every step on the way', () => {
      render(1);
      jest.advanceTimersByTime(200);
      render(2);
      jest.advanceTimersByTime(200);
      render(3);
      fixture.detectChanges();

      expect(live()?.textContent?.trim()).toBe('');

      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      fixture.detectChanges();

      expect(live()?.textContent?.trim()).toBe('3 selected');
    });

    it('announces a return to the count it announced last, 3 then 4 then back to 3', () => {
      render(3);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      render(4);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      fixture.detectChanges();
      expect(live()?.textContent?.trim()).toBe('4 selected');

      render(3);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      fixture.detectChanges();
      expect(live()?.textContent?.trim()).toBe('3 selected');

      // Settling on the value already in the live region empties it for a moment, so the same words are
      // read again rather than left unchanged.
      render(4);
      render(3);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      fixture.detectChanges();
      expect(live()?.textContent?.trim()).toBe('');
      jest.advanceTimersByTime(BULK_COUNT_REANNOUNCE_MS);
      fixture.detectChanges();
      expect(live()?.textContent?.trim()).toBe('3 selected');
    });

    it('still announces a later settled change, such as Select all', () => {
      render(1);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      render(12);
      jest.advanceTimersByTime(BULK_COUNT_SETTLE_MS);
      fixture.detectChanges();

      expect(live()?.textContent?.trim()).toBe('12 selected');
    });
  });

  it('asks its list to act', () => {
    render(3);
    const calls: string[] = [];
    fixture.componentInstance.selectAll.subscribe(() => calls.push('select'));
    fixture.componentInstance.deselectAll.subscribe(() => calls.push('deselect'));
    fixture.componentInstance.changeRadius.subscribe(() => calls.push('radius'));
    fixture.componentInstance.deleteSelected.subscribe(() => calls.push('delete'));

    byText('Select all').click();
    byText('Deselect all').click();
    byText('Change radius').click();
    deleteButton().click();

    expect(calls).toEqual(['select', 'deselect', 'radius', 'delete']);
  });

  describe('Deliver to menu', () => {
    it('is absent for a person with no link', () => {
      render(2);

      expect(el().querySelector('.bulk-deliver')).toBeNull();
    });

    it('offers the two apps when linked, disabled with nothing selected', () => {
      fixture.componentRef.setInput('canMove', true);
      render(0);
      const trigger = el().querySelector<HTMLButtonElement>('.bulk-deliver')!;

      expect(trigger).not.toBeNull();
      expect(trigger.disabled).toBe(true);
    });

    it('reports the chosen side', () => {
      fixture.componentRef.setInput('canMove', true);
      render(2);
      const chosen: string[] = [];
      fixture.componentInstance.moveSelected.subscribe(s => chosen.push(s));

      el().querySelector<HTMLButtonElement>('.bulk-deliver')!.click();
      fixture.detectChanges();
      const items = Array.from(document.querySelectorAll<HTMLButtonElement>('.bulk-deliver-item'));
      items.find(i => i.dataset['app'] === 'telegram')!.click();

      expect(chosen).toEqual(['telegram']);
    });
  });

  /** Shape 2: one bulk bar, on all ten lists, counting the selected cards still on screen. */
  it.each([
    'pokemon/pokemon',
    'raids/raid',
    'quests/quest',
    'invasions/invasion',
    'pokestop-events/pokestop-event',
    'lures/lure',
    'nests/nest',
    'gyms/gym',
    'max-battles/max-battle',
    'fort-changes/fort-change',
  ])('%s list uses it and draws no bulk bar of its own', list => {
    const html = readFileSync(join(__dirname, '../../../modules', `${list}-list.component.html`), 'utf8');
    const scss = readFileSync(join(__dirname, '../../../modules', `${list}-list.component.scss`), 'utf8');

    expect(html.match(/<app-bulk-action-bar/g)?.length).toBe(1);
    expect(html).toContain('[count]="actionIds().length"');
    expect(html).toContain('[anySelected]="selectedIds().size > 0"');
    expect(html).toContain('(changeRadius)="bulkUpdateDistance()"');
    expect(html).toContain('(deleteSelected)="bulkDelete()"');
    expect(html).not.toContain('class="bulk-toolbar"');
    expect(scss).not.toContain('.bulk-toolbar');
  });
});
