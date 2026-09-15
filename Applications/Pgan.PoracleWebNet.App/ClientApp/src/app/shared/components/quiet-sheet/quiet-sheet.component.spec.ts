import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { QuietSheetComponent, QuietSheetData } from './quiet-sheet.component';
import { MuteService, QUIET_DURATIONS } from '../../../core/services/mute.service';

describe('QuietSheetComponent', () => {
  let fixture: ComponentFixture<QuietSheetComponent>;
  const dialogRef = { close: jest.fn() };

  const build = (data: Partial<QuietSheetData> = {}) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MAT_DIALOG_DATA,
          useValue: { isQuiet: false, scope: 'gym', subject: 'Bareena Park', value: 'abc123', ...data },
        },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MuteService, useValue: { durationLabel: jest.fn((m: number) => `${m} min`) } },
        provideTranslateService(),
      ],
      imports: [QuietSheetComponent],
    });
    fixture = TestBed.createComponent(QuietSheetComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  beforeEach(() => dialogRef.close.mockClear());

  /** 60 is PoracleNG's own default, so the sheet agreeing with it costs the user a decision. */
  it('defaults to one hour', () => {
    expect(build().minutes()).toBe(60);
  });

  it('offers six durations and no free-text box', () => {
    build();

    expect(QUIET_DURATIONS).toHaveLength(6);
    expect(fixture.nativeElement.querySelectorAll('mat-chip-option')).toHaveLength(6);
    expect(fixture.nativeElement.querySelector('input')).toBeNull();
  });

  /**
   * Said once, plainly. A countdown that vanishes early reads as time passing only to somebody who was
   * told the store does not survive a restart.
   */
  it('states that quiet periods do not survive a restart', () => {
    build();

    expect(fixture.nativeElement.textContent).toContain('QUIET.VOLATILITY_NOTE');
  });

  it('closes with the chosen duration', () => {
    const sheet = build();
    sheet.minutes.set(240);
    sheet.confirm();

    expect(dialogRef.close).toHaveBeenCalledWith({ minutes: 240, type: 'quiet' });
  });

  /** Re-quieting is not an error, so the same sheet carries the way back out. */
  it('offers resume only when the subject is already quiet', () => {
    build();
    expect(fixture.nativeElement.textContent).not.toContain('QUIET.RESUME');

    build({ isQuiet: true });
    expect(fixture.nativeElement.textContent).toContain('QUIET.RESUME');
  });

  it('closes with a resume request', () => {
    build({ isQuiet: true }).resume();

    expect(dialogRef.close).toHaveBeenCalledWith({ type: 'resume' });
  });

  it('preselects the current duration when re-quieting', () => {
    expect(build({ currentMinutes: 480, isQuiet: true }).minutes()).toBe(480);
  });
});
