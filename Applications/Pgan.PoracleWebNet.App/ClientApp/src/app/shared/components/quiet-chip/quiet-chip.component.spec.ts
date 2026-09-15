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

  const buildAs = (appearance: 'chip' | 'icon' | 'trailing') => {
    fixture = TestBed.createComponent(QuietChipComponent);
    fixture.componentRef.setInput('scope', 'area');
    fixture.componentRef.setInput('value', 'downtown - richmond');
    fixture.componentRef.setInput('appearance', appearance);
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

  /**
   * The three appearances, asserted together because the trailing one was carved out of the icon one
   * and the seven card-action usages depend on nothing about the icon branch having moved. See #865.
   */
  describe('appearance', () => {
    /**
     * A card's action row is a row of 40px icon buttons and this is one of them. Measured at the time
     * of #865: 40x40 with an 8px pad, which is correct there and only there.
     */
    it('keeps the card action row on a full icon button', () => {
      buildAs('icon');

      expect(fixture.nativeElement.querySelector('button.mat-mdc-icon-button')).not.toBeNull();
    });

    it('still shows the amber pill in a card action row once the subject is quiet', () => {
      mutes.remainingFor.mockReturnValue(2820);
      buildAs('icon');

      expect(fixture.nativeElement.querySelector('.quiet-chip-on')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.quiet-chip-label').textContent).toContain('QUIET.CHIP_QUIET');
    });

    it('carries an icon and a label on a list row in both states', () => {
      buildAs('chip');
      expect(fixture.nativeElement.querySelector('.quiet-chip-label').textContent).toContain('QUIET.CHIP_ACTION');

      mutes.remainingFor.mockReturnValue(2820);
      buildAs('chip');
      expect(fixture.nativeElement.querySelector('.quiet-chip-label').textContent).toContain('QUIET.CHIP_QUIET');
    });

    /**
     * Inside a mat-chip the icon button was 40px tall in a 32px chip, overflowing it by 4px top and
     * bottom and leaving 16px of dead space before the remove icon. The trailing form is the compact
     * control instead.
     */
    it('drops the icon button inside a chip', () => {
      buildAs('trailing');

      expect(fixture.nativeElement.querySelector('button.mat-mdc-icon-button')).toBeNull();
      expect(fixture.nativeElement.querySelector('.quiet-chip-trailing')).not.toBeNull();
    });

    /** "downtown - richmond . Quiet" would double the width of an already-wrapping bar. */
    it('shows no label inside a chip until there is a countdown to show', () => {
      buildAs('trailing');

      expect(fixture.nativeElement.querySelector('.quiet-chip-label')).toBeNull();
    });

    it('shows the countdown inside a chip once the subject is quiet', () => {
      mutes.remainingFor.mockReturnValue(2820);
      buildAs('trailing');

      expect(fixture.nativeElement.querySelector('.quiet-chip-label').textContent).toContain('QUIET.CHIP_QUIET');
    });

    /** Both states sit in the same bar, so whatever sizes them has to size both. */
    it('keeps both states on the same trailing control', () => {
      buildAs('trailing');
      expect(fixture.nativeElement.querySelector('.quiet-chip-trailing')).not.toBeNull();

      mutes.remainingFor.mockReturnValue(2820);
      buildAs('trailing');
      expect(fixture.nativeElement.querySelector('.quiet-chip-trailing.quiet-chip-on')).not.toBeNull();
    });
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
