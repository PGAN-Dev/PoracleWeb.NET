import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { AlarmEmptyStateComponent } from './alarm-empty-state.component';

describe('AlarmEmptyStateComponent', () => {
  let fixture: ComponentFixture<AlarmEmptyStateComponent>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideTranslateService()], imports: [AlarmEmptyStateComponent] });
    fixture = TestBed.createComponent(AlarmEmptyStateComponent);
    fixture.componentRef.setInput('icon', 'park');
    fixture.componentRef.setInput('titleKey', 'NESTS.EMPTY_TITLE');
    fixture.componentRef.setInput('bodyKey', 'NESTS.EMPTY_SUBTITLE');
    fixture.detectChanges();
  });

  it('says there are none yet, what one would send, and offers the next step', () => {
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.empty-title')?.textContent).toContain('NESTS.EMPTY_TITLE');
    expect(el.querySelector('.empty-subtitle')?.textContent).toContain('NESTS.EMPTY_SUBTITLE');
    expect(el.querySelector('.empty-action')?.textContent).toContain('ALARM.EMPTY_ADD');
  });

  it('asks its list to open the add dialog', () => {
    const add = jest.fn();
    fixture.componentInstance.add.subscribe(add);

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.empty-action')!.click();

    expect(add).toHaveBeenCalledTimes(1);
  });

  /** Shape 2: one empty state, in all ten lists (two on raids, one per tab). */
  it.each([
    ['pokemon/pokemon', 1],
    ['raids/raid', 2],
    ['quests/quest', 1],
    ['invasions/invasion', 1],
    ['lures/lure', 1],
    ['nests/nest', 1],
    ['gyms/gym', 1],
    ['fort-changes/fort-change', 1],
    ['pokestop-events/pokestop-event', 1],
    ['max-battles/max-battle', 1],
  ])('%s list uses it and draws no empty state of its own', (list, count) => {
    const html = readFileSync(join(__dirname, '../../../modules', `${list}-list.component.html`), 'utf8');

    expect(html.match(/<app-alarm-empty-state/g)?.length).toBe(count);
    expect(html).not.toContain('class="empty-state"');
  });
});
