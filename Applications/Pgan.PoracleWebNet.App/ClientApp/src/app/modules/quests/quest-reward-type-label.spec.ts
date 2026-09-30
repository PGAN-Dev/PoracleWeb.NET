import * as fs from 'fs';
import * as path from 'path';

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';

import { QuestListComponent } from './quest-list.component';

const en = JSON.parse(fs.readFileSync(path.join(__dirname, '../../../assets/i18n/en.json'), 'utf8'));

/**
 * The quest card's reward-type pill had no case for stardust, and its fallback -- "Quest Type:" --
 * carried no `{{type}}`, so a stardust rule read "Quest Type:" with nothing after it.
 */
describe('QuestListComponent reward type label', () => {
  let component: QuestListComponent;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', en);
    translate.use('en');
    component = TestBed.createComponent(QuestListComponent).componentInstance;
  });

  /** Every reward type the add dialog can create: Pokemon, item, stardust, candy, pokecoins, mega energy. */
  it.each([
    [7, 'Pokemon'],
    [2, 'Item'],
    [3, 'Stardust'],
    [4, 'Candy'],
    [8, 'PokéCoins'],
    [12, 'Mega Energy'],
  ])('labels reward type %i as %s', (rewardType, label) => {
    expect(component.getRewardTypeLabel(rewardType)).toBe(label);
  });

  /** A type only the bot can create still says which type it is, rather than trailing off. */
  it('names an unknown reward type by number', () => {
    expect(component.getRewardTypeLabel(1)).toBe('Quest Type: 1');
  });
});
