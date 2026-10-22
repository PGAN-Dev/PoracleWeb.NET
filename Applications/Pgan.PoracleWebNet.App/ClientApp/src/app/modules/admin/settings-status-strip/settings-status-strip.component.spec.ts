import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { SettingsStatusStripComponent } from './settings-status-strip.component';
import { PoracleServerProfile, RuleRepairStatus } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';
import { RuleRepairService } from '../../../core/services/rule-repair.service';

const upToDate = { latest: null, running: null, state: 'UpToDate' as const };

const profile = (over: Partial<PoracleServerProfile> = {}): PoracleServerProfile => ({
  belowMinimum: false,
  capabilities: {},
  checkedAt: '2026-10-22T10:00:00Z',
  minimumSupported: '5.4.0',
  poracleUpdate: upToDate,
  reachable: true,
  version: '5.4.0',
  web: { buildDate: null, revision: null, version: '2.30.0' },
  webUpdate: upToDate,
  ...over,
});

const repair = (over: Partial<RuleRepairStatus> = {}): RuleRepairStatus => ({
  applied: null,
  cannotEdit: [],
  cannotEditCount: 0,
  error: null,
  finishedAt: null,
  humansDone: 0,
  humansTotal: 0,
  phase: 'Idle',
  repairable: [],
  repairableCount: 0,
  skipped: [],
  skippedCount: 0,
  startedAt: null,
  ...over,
});

/**
 * The Versions and Rule repair cards used to open the settings page at full height. They are one line
 * now, and that line has to tell the same truths the cards did: too old, unreachable, rules to repair.
 */
describe('SettingsStatusStripComponent', () => {
  function setup(server: PoracleServerProfile | null, rules: RuleRepairStatus | null = repair()) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AdminService,
          useValue: { getServerProfile: jest.fn(() => (server ? of(server) : throwError(() => new Error('down')))) },
        },
        {
          provide: RuleRepairService,
          useValue: {
            apply: jest.fn(),
            getStatus: jest.fn(() => (rules ? of(rules) : throwError(() => new Error('down')))),
            scan: jest.fn(),
          },
        },
        { provide: MatDialog, useValue: { open: jest.fn() } },
      ],
      imports: [SettingsStatusStripComponent],
    });
    const fixture = TestBed.createComponent(SettingsStatusStripComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  const text = (fixture: ReturnType<typeof setup>, id: string) =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-test="part-${id}"]`)?.textContent?.trim() ?? '';

  it('reads as one line with both versions and the rule state', () => {
    const fixture = setup(profile(), repair({ phase: 'Scanned' }));

    expect(text(fixture, 'web')).toContain('ADMIN.STATUS_STRIP.WEB');
    expect(text(fixture, 'poracle')).toContain('ADMIN.STATUS_STRIP.PORACLE');
    expect(text(fixture, 'rules')).toContain('ADMIN.STATUS_STRIP.RULES_NOTHING');
  });

  it('keeps the cards mounted but hidden until Details is pressed', () => {
    const fixture = setup(profile());
    const el = fixture.nativeElement as HTMLElement;
    const details = el.querySelector('[data-test="status-details"]') as HTMLElement;
    const toggle = el.querySelector('[data-test="status-details-toggle"]') as HTMLButtonElement;

    expect(details.hidden).toBe(true);
    expect(details.querySelector('app-server-profile-card')).toBeTruthy();
    expect(details.querySelector('app-rule-repair-card')).toBeTruthy();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    expect(details.hidden).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });

  it('marks a PoracleNG below the minimum as an error', () => {
    const fixture = setup(profile({ belowMinimum: true, version: '5.2.1' }));
    const part = (fixture.nativeElement as HTMLElement).querySelector('[data-test="part-poracle"]')!;

    expect(part.classList).toContain('tone-error');
    expect(part.textContent).toContain('ADMIN.STATUS_STRIP.TOO_OLD');
  });

  it('marks an unreachable PoracleNG as a warning, and a failed probe the same way', () => {
    for (const server of [profile({ reachable: false }), null]) {
      const fixture = setup(server);
      const part = (fixture.nativeElement as HTMLElement).querySelector('[data-test="part-poracle"]')!;
      expect(part.classList).toContain('tone-warn');
      expect(part.textContent).toContain('ADMIN.STATUS_STRIP.UNREACHABLE');
    }
  });

  it('leaves a healthy PoracleNG uncoloured', () => {
    const fixture = setup(profile());
    const part = (fixture.nativeElement as HTMLElement).querySelector('[data-test="part-poracle"]')!;

    expect(part.classList).not.toContain('tone-warn');
    expect(part.classList).not.toContain('tone-error');
  });

  it.each([
    [repair({ phase: 'Idle' }), 'RULES_NOT_SCANNED'],
    [repair({ humansDone: 3, humansTotal: 10, phase: 'Scanning' }), 'RULES_SCANNING'],
    [repair({ phase: 'Scanned', repairableCount: 4 }), 'RULES_TO_REPAIR'],
    [repair({ phase: 'Scanned', skippedCount: 2 }), 'RULES_INCOMPLETE'],
    [repair({ phase: 'Failed' }), 'RULES_FAILED'],
    [repair({ phase: 'Applied' }), 'RULES_REPAIRED'],
  ])('says what rule repair found (%#)', (rules, key) => {
    const fixture = setup(profile(), rules);
    expect(text(fixture, 'rules')).toContain(`ADMIN.STATUS_STRIP.${key}`);
  });

  it('never says "nothing to repair" when users were skipped', () => {
    const fixture = setup(profile(), repair({ phase: 'Scanned', skippedCount: 2 }));
    expect(text(fixture, 'rules')).not.toContain('RULES_NOTHING');
  });

  it('says the rule state is unavailable when it cannot be read', () => {
    const fixture = setup(profile(), null);
    expect(text(fixture, 'rules')).toContain('ADMIN.STATUS_STRIP.RULES_UNAVAILABLE');
  });
});
