import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuietChipComponent } from './quiet-chip.component';
import { Mute, MuteService } from '../../../core/services/mute.service';

describe('QuietChipComponent', () => {
  let fixture: ComponentFixture<QuietChipComponent>;
  let mutes: {
    capable: jest.Mock;
    countdownLabel: jest.Mock;
    find: jest.Mock;
    quiet: jest.Mock;
    refresh: jest.Mock;
    remainingFor: jest.Mock;
    resume: jest.Mock;
  };
  const dialogRef = { afterClosed: jest.fn(() => of(undefined)) };
  const dialog = { open: jest.fn(() => dialogRef) };

  const build = (scope = 'gym', value = 'abc123', subject = '') => {
    fixture = TestBed.createComponent(QuietChipComponent);
    fixture.componentRef.setInput('scope', scope);
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('subject', subject);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  beforeEach(() => {
    mutes = {
      capable: jest.fn(() => true),
      countdownLabel: jest.fn(() => '47m'),
      find: jest.fn((): Mute | undefined => undefined),
      quiet: jest.fn(() => of(false)),
      refresh: jest.fn(),
      remainingFor: jest.fn((): null | number => null),
      resume: jest.fn(() => of(undefined)),
    };
    dialogRef.afterClosed.mockReturnValue(of(undefined));
    dialog.open.mockClear();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MuteService, useValue: mutes },
        { provide: MatDialog, useValue: dialog },
        { provide: TranslateService, useValue: { instant: jest.fn((key: string) => key) } },
      ],
      imports: [QuietChipComponent],
    });
  });

  /**
   * There is nothing useful to say about a server feature the operator has not got yet, and a control
   * that 404s on every press is worse than its absence.
   */
  it('renders nothing on a server that cannot do quiet periods', () => {
    mutes.capable.mockReturnValue(false);
    build();

    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('offers the control when the subject is not quiet', () => {
    const chip = build();

    expect(chip.isQuiet()).toBe(false);
    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.quiet-chip-on')).toBeNull();
  });

  it('shows a countdown when the subject is quiet', () => {
    mutes.remainingFor.mockReturnValue(2820);
    const chip = build();

    expect(chip.isQuiet()).toBe(true);
    expect(chip.countdown()).toBe('47m');
    expect(fixture.nativeElement.querySelector('.quiet-chip-on')).not.toBeNull();
  });

  /** Every appearance refetches: the store lives in the processor's memory and can empty between views. */
  it('refreshes the list as it appears', () => {
    build();

    expect(mutes.refresh).toHaveBeenCalled();
  });

  it('names the subject in the user words when the surface knows it', () => {
    const chip = build('gym', 'abc123', 'Bareena Park');

    expect(chip.subjectLabel()).toBe('Bareena Park');
  });

  /** A raw id is a database row, not a sentence. Fall back to a generic phrase per scope. */
  it('falls back to a generic phrase rather than showing a raw identifier', () => {
    const chip = build('station', '0f3a91c2e4');

    expect(chip.subjectLabel()).toBe('QUIET.SUBJECT_STATION');
  });

  it('quiets for the duration the sheet came back with', () => {
    dialogRef.afterClosed.mockReturnValue(of({ minutes: 240, type: 'quiet' }));
    build().open();

    expect(mutes.quiet).toHaveBeenCalledWith('gym', 'abc123', 240);
  });

  it('resumes when the sheet asked for it', () => {
    dialogRef.afterClosed.mockReturnValue(of({ type: 'resume' }));
    build().open();

    expect(mutes.resume).toHaveBeenCalledWith('gym', 'abc123');
  });

  it('does nothing when the sheet is dismissed', () => {
    build().open();

    expect(mutes.quiet).not.toHaveBeenCalled();
    expect(mutes.resume).not.toHaveBeenCalled();
  });

  /** The chip sits inside cards and rows that navigate or toggle on click. */
  it('stops the click reaching the surface behind it', () => {
    const event = { stopPropagation: jest.fn() } as unknown as Event;
    build().open(event);

    expect(event.stopPropagation).toHaveBeenCalled();
  });
});
