import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QuietListSheetComponent } from './quiet-list-sheet.component';
import { MasterDataService } from '../../../core/services/masterdata.service';
import { Mute, MuteService } from '../../../core/services/mute.service';
import { ScannerService } from '../../../core/services/scanner.service';

describe('QuietListSheetComponent', () => {
  let fixture: ComponentFixture<QuietListSheetComponent>;
  const resume = jest.fn(() => of(undefined));
  const resumeAll = jest.fn(() => of(undefined));
  const getGymById = jest.fn(() => of(null));

  const build = (mutes: Mute[]) => {
    const list = signal(mutes);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        {
          provide: MuteService,
          useValue: {
            countdownLabel: jest.fn(() => '47m'),
            mutes: list,
            refresh: jest.fn(),
            resume,
            resumeAll,
          },
        },
        { provide: MasterDataService, useValue: { getPokemonName: jest.fn(() => 'Pikachu') } },
        { provide: ScannerService, useValue: { getGymById } },
      ],
      imports: [QuietListSheetComponent],
    });
    fixture = TestBed.createComponent(QuietListSheetComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  const aMute = (overrides: Partial<Mute> = {}): Mute => ({
    expiresAt: Math.floor(Date.now() / 1000) + 2820,
    remainingSecs: 2820,
    scope: 'gym',
    value: 'abc123',
    ...overrides,
  });

  beforeEach(() => {
    resume.mockClear();
    resumeAll.mockClear();
    getGymById.mockClear();
  });

  /** An empty screen is an invitation, and the second line says where to go. */
  it('says nothing is quiet and where to start', () => {
    build([]);

    expect(fixture.nativeElement.textContent).toContain('QUIET.EMPTY_TITLE');
    expect(fixture.nativeElement.textContent).toContain('QUIET.EMPTY_HINT');
  });

  /**
   * The whole point of this sheet. A list that hides what it cannot create is useless to somebody
   * trying to work out why they are getting nothing.
   */
  it('lists scopes this app cannot create, including one set from the Discord bot', () => {
    const sheet = build([aMute({ scope: 'everything', value: null }), aMute({ scope: 'pokestop', value: 'stop-1' })]);

    expect(sheet.rows().map(row => row.mute.scope)).toEqual(['everything', 'pokestop']);
    expect(sheet.rows()[0].subject).toBe('QUIET.SUBJECT_EVERYTHING');
  });

  it('names a species rather than showing its dex id', () => {
    expect(build([aMute({ scope: 'pokemon', value: '25' })]).rows()[0].subject).toBe('Pikachu');
  });

  /** The scanner DB is optional, so an unresolvable gym still has to be listed and liftable. */
  it('falls back to the gym id when the scanner does not know it', () => {
    expect(build([aMute()]).rows()[0].subject).toBe('abc123');
  });

  it('lifts one quiet period by its scope and stored value', () => {
    const sheet = build([aMute({ scope: 'area', value: 'Aberdeen' })]);
    sheet.resume(sheet.rows()[0]);

    expect(resume).toHaveBeenCalledWith('area', 'Aberdeen');
  });

  it('offers resume everything only when more than one thing is quiet', () => {
    build([aMute()]);
    expect(fixture.nativeElement.textContent).not.toContain('QUIET.RESUME_ALL');

    build([aMute(), aMute({ scope: 'area', value: 'Aberdeen' })]);
    expect(fixture.nativeElement.textContent).toContain('QUIET.RESUME_ALL');
  });
});
