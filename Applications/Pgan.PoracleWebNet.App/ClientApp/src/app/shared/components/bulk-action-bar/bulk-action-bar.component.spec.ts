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
        BULK_MORE: 'More',
        BULK_MORE_LABEL: 'More actions for the selected alarms',
        DELETE: 'Delete',
        DELETE_SELECTED: 'Delete {{count}}',
        DELETE_SELECTED_LABEL_ONE: 'Delete {{count}} alarm',
        DELETE_SELECTED_LABEL_OTHER: 'Delete {{count}} alarms',
        DELIVER_ALREADY_THERE: 'All already there',
        DELIVER_TO_APP: 'Deliver to {{app}}',
        DELIVER_TO_MENU: 'Deliver to…',
        DESELECT_ALL: 'Deselect all',
        SELECT_ALL: 'Select all',
        SELECTED_COUNT: '{{count}} selected',
        UPDATE_DISTANCE: 'Change radius',
      },
      DELIVER_TO: { DISCORD: 'Discord', TELEGRAM: 'Telegram' },
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

  describe('Deliver to menu marks the app every selected alarm is already on', () => {
    const openDeliver = () => {
      el().querySelector<HTMLButtonElement>('.bulk-deliver')!.click();
      fixture.detectChanges();
      return Array.from(document.querySelectorAll<HTMLButtonElement>('.bulk-deliver-item'));
    };

    afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach(c => (c.innerHTML = '')));

    it('draws each app with its glyph', () => {
      fixture.componentRef.setInput('canMove', true);
      render(2);

      const items = openDeliver();
      expect(items.map(i => i.querySelector('app-app-glyph')?.getAttribute('data-app'))).toEqual(['discord', 'telegram']);
    });

    it('disables the app they are all on and says so, leaving the other one open', () => {
      fixture.componentRef.setInput('canMove', true);
      fixture.componentRef.setInput('allOn', 'telegram');
      render(2);

      const items = openDeliver();
      const telegram = items.find(i => i.dataset['app'] === 'telegram')!;
      const discord = items.find(i => i.dataset['app'] === 'discord')!;
      expect(telegram.disabled).toBe(true);
      expect(telegram.textContent).toContain('All already there');
      expect(discord.disabled).toBe(false);
      expect(discord.textContent).not.toContain('All already there');
    });

    it('leaves both open for a mixed selection', () => {
      fixture.componentRef.setInput('canMove', true);
      fixture.componentRef.setInput('allOn', null);
      render(2);

      expect(openDeliver().every(i => !i.disabled)).toBe(true);
    });
  });

  describe('on a phone', () => {
    const scss = readFileSync(join(__dirname, 'bulk-action-bar.component.scss'), 'utf8');
    const phone = scss.slice(scss.indexOf('@media (max-width: 599px)'));

    afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach(c => (c.innerHTML = '')));

    it('folds Deliver to and Change radius into More for a linked person, so the row holds More and Delete', () => {
      fixture.componentRef.setInput('canMove', true);
      render(2);

      expect(el().querySelector('.bulk-toolbar')?.classList).toContain('bulk-can-move');
      const more = el().querySelector<HTMLButtonElement>('.bulk-more')!;
      expect(more.textContent).toContain('More');
      expect(more.getAttribute('aria-label')).toBe('More actions for the selected alarms');
      expect(phone).toMatch(/\.bulk-can-move\s+\.bulk-deliver,\s*\.bulk-can-move\s+\.bulk-radius\s*\{\s*display:\s*none/);
      expect(phone).toMatch(/\.bulk-can-move\s+\.bulk-more\s*\{\s*display:\s*inline-flex/);
      // Off the phone, More stays hidden and the three actions stand on their own.
      expect(scss.slice(0, scss.indexOf('@media (max-width: 599px)'))).toMatch(/\.bulk-more\s*\{\s*display:\s*none/);
    });

    it('offers both apps and Change radius from More, and reports each', () => {
      fixture.componentRef.setInput('canMove', true);
      fixture.componentRef.setInput('allOn', 'discord');
      render(2);
      const calls: string[] = [];
      fixture.componentInstance.moveSelected.subscribe(s => calls.push(s));
      fixture.componentInstance.changeRadius.subscribe(() => calls.push('radius'));

      el().querySelector<HTMLButtonElement>('.bulk-more')!.click();
      fixture.detectChanges();
      const items = Array.from(document.querySelectorAll<HTMLButtonElement>('.bulk-more-item'));
      expect(items.map(i => i.textContent?.trim())).toEqual([
        expect.stringContaining('Deliver to Discord'),
        expect.stringContaining('Deliver to Telegram'),
        expect.stringContaining('Change radius'),
      ]);
      expect(items[0].disabled).toBe(true);
      items[1].click();
      items[2].click();

      expect(calls).toEqual(['telegram', 'radius']);
    });

    it('keeps the unlinked bar as it was: no More, and Change radius beside Delete', () => {
      render(2);

      expect(el().querySelector('.bulk-more')).toBeNull();
      expect(el().querySelector('.bulk-toolbar')?.classList).not.toContain('bulk-can-move');
      expect(byText('Change radius')).toBeDefined();
    });

    it('keeps the count, Select all and Deselect all on one row, as icons that keep their names', () => {
      render(1);

      const select = el().querySelector<HTMLButtonElement>('.bulk-select-all')!;
      const deselect = el().querySelector<HTMLButtonElement>('.bulk-deselect-all')!;
      expect(select.getAttribute('aria-label')).toBe('Select all');
      expect(deselect.getAttribute('aria-label')).toBe('Deselect all');
      expect(select.querySelector('mat-icon')?.textContent?.trim()).toBe('select_all');
      expect(deselect.querySelector('mat-icon')?.textContent?.trim()).toBe('deselect');
      // German "Alle auswählen" and "Alle abwählen" took a row each, and the bar covered the first card.
      expect(phone).toMatch(/\.bulk-selection\s*\{[^}]*flex-wrap:\s*nowrap/);
      expect(phone).toMatch(/\.bulk-select-label\s*\{\s*display:\s*none/);
      expect(phone).toMatch(/\.bulk-select-icon\s*\{[^}]*display:\s*inline-block/);
      // Off the phone the words stay, and the icons stay out of the way.
      const wide = scss.slice(0, scss.indexOf('@media (max-width: 599px)'));
      expect(wide).toMatch(/\.bulk-select-icon\s*\{\s*display:\s*none/);
      expect(select.querySelector('.bulk-select-label')?.textContent?.trim()).toBe('Select all');
    });

    it('puts "All already there" under the app name, not squeezed beside it', () => {
      fixture.componentRef.setInput('canMove', true);
      fixture.componentRef.setInput('allOn', 'telegram');
      render(2);

      el().querySelector<HTMLButtonElement>('.bulk-more')!.click();
      fixture.detectChanges();
      const telegram = Array.from(document.querySelectorAll<HTMLButtonElement>('.bulk-more-item')).find(
        i => i.dataset['app'] === 'telegram',
      )!;
      const text = telegram.querySelector('.bulk-app-text')!;
      expect(text.children[0].textContent?.trim()).toBe('Deliver to Telegram');
      expect(text.children[1].classList).toContain('bulk-app-note');
      expect(scss).toMatch(/\.bulk-app-text\s*\{[^}]*flex-direction:\s*column/);
    });

    it('never lets a label wrap or push Delete off the edge', () => {
      expect(phone).toMatch(/flex:\s*1 1 0/);
      expect(phone).toMatch(/min-width:\s*0/);
      expect(phone).toMatch(/white-space:\s*nowrap/);
      expect(phone).toMatch(/text-overflow:\s*ellipsis/);
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
    expect(html).toContain('[allOn]="selectionApp()"');
    expect(html).not.toContain('class="bulk-toolbar"');
    expect(scss).not.toContain('.bulk-toolbar');
  });
});
