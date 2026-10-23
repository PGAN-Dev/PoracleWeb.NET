import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { TranslatePluralPipe } from './translate-plural.pipe';

describe('TranslatePluralPipe', () => {
  let pipe: TranslatePluralPipe;
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideTranslateService(), TranslatePluralPipe] });
    pipe = TestBed.inject(TranslatePluralPipe);
    translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { X: { N_ONE: '{{count}} alarm', N_OTHER: '{{count}} alarms' } });
    translate.setTranslation('pl', {
      X: { N_FEW: '{{count}} alarmy', N_MANY: '{{count}} alarmów', N_ONE: '{{count}} alarm', N_OTHER: '{{count}} alarmów' },
    });
  });

  it('follows the display language when it changes under the same input', () => {
    translate.use('en');
    expect(pipe.transform('X.N', 1)).toBe('1 alarm');
    expect(pipe.transform('X.N', 3)).toBe('3 alarms');

    translate.use('pl');
    expect(pipe.transform('X.N', 3)).toBe('3 alarmy');
    expect(pipe.transform('X.N', 5)).toBe('5 alarmów');
  });

  it('passes extra params through', () => {
    translate.setTranslation('en', { Y: { M_ONE: '{{count}} of {{name}}', M_OTHER: '{{count}} of {{name}}s' } });
    translate.use('en');
    expect(pipe.transform('Y.M', 2, { name: 'pick' })).toBe('2 of picks');
  });
});
