import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { provideTranslateService } from '@ngx-translate/core';

import { PHONE_BOTTOM_SHEET, WhereSheetComponent, WhereSheetData, whereSheetConfig } from './where-sheet.component';
import { ConfigService } from '../../../core/services/config.service';
import { AlarmScope } from '../../utils/alarm-scope';
import { ScopePickerComponent } from '../scope-picker/scope-picker.component';

describe('WhereSheetComponent', () => {
  let fixture: ComponentFixture<WhereSheetComponent>;
  let dialogRef: { close: jest.Mock };

  function create(data: WhereSheetData): WhereSheetComponent {
    dialogRef = { close: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test' } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [WhereSheetComponent],
    });
    fixture = TestBed.createComponent(WhereSheetComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function picker(): ScopePickerComponent {
    return fixture.debugElement.query(By.directive(ScopePickerComponent)).componentInstance as ScopePickerComponent;
  }

  function saveDisabled(): boolean {
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('mat-dialog-actions button');
    return (buttons[buttons.length - 1] as HTMLButtonElement).disabled;
  }

  it('turns Save off for a radius past 100 km and back on once it is fixed', () => {
    create({ profileAreas: [], scope: { distanceKm: 5, mode: 'profile' } });
    expect(saveDisabled()).toBe(false);

    picker().distanceKm.set(500);
    fixture.detectChanges();
    expect(saveDisabled()).toBe(true);

    picker().distanceKm.set(50);
    fixture.detectChanges();
    expect(saveDisabled()).toBe(false);
  });

  it('saves an alarm already stored past 100 km without making its owner change the radius', () => {
    const sheet = create({ profileAreas: [], scope: { distanceKm: 500, mode: 'profile' } });

    expect(saveDisabled()).toBe(false);
    sheet.save();
    expect(dialogRef.close).toHaveBeenCalledWith({ distanceKm: 500, mode: 'profile' });
  });

  it('holds a changed radius on such an alarm to the range', () => {
    create({ profileAreas: [], scope: { distanceKm: 500, mode: 'profile' } });

    picker().distanceKm.set(600);
    fixture.detectChanges();
    expect(saveDisabled()).toBe(true);
  });

  it.each([
    [{ distanceKm: 0.04, mode: 'profile' as const }],
    [{ distanceKm: 0.03, mode: 'place' as const, placeLabel: 'work' }],
    [{ distanceKm: 1.234, mode: 'profile' as const }],
  ])('saves a stored radius under 0.1 km or off the 100 m grid as it was: %j', scope => {
    const sheet = create({ profileAreas: [], scope });

    expect(saveDisabled()).toBe(false);
    sheet.save();
    expect(dialogRef.close).toHaveBeenCalledWith(scope);
  });
});

describe('whereSheetConfig', () => {
  const read = (relative: string) => readFileSync(join(__dirname, '../../../modules', relative), 'utf8');
  const data: WhereSheetData = { profileAreas: [], scope: { areas: [], mode: 'areas' } as AlarmScope };

  /** On a phone the sheet is three radio rows; full screen hid the card it was changing. */
  it('opens as a bottom sheet on a phone and a 520px dialog elsewhere', () => {
    const config = whereSheetConfig(data);

    expect(config.panelClass).toBe(PHONE_BOTTOM_SHEET);
    expect(config.width).toBe('520px');
    expect(config.autoFocus).toBe(false);
    expect(config.data).toBe(data);
  });

  it('has a phone rule in the global styles that anchors it to the bottom at its own height', () => {
    const styles = readFileSync(join(__dirname, '../../../../styles.scss'), 'utf8');

    expect(styles).toContain(`.cdk-global-overlay-wrapper:has(> .${PHONE_BOTTOM_SHEET})`);
    expect(styles).toContain(`.cdk-overlay-pane.${PHONE_BOTTOM_SHEET}`);
  });

  /** Shape 2: every list's scope chip opens the same sheet the same way. */
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
  ])('%s list opens it with whereSheetConfig', list => {
    const ts = read(`${list}-list.component.ts`);

    expect(ts).toContain('.open(WhereSheetComponent, whereSheetConfig(data))');
    expect(ts).not.toMatch(/open\(WhereSheetComponent, \{/);
  });
});
