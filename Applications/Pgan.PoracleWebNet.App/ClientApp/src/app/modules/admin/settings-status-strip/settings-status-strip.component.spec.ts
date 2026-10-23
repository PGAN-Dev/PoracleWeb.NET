import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { SettingsStatusStripComponent } from './settings-status-strip.component';
import { PoracleServerProfile, RuleRepairItem, RuleRepairStatus } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';
import { RuleRepairService } from '../../../core/services/rule-repair.service';
import { RuleRepairCardComponent } from '../rule-repair-card/rule-repair-card.component';

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
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', {
      ADMIN: {
        STATUS_STRIP: {
          PART_CONFLICTS: 'conflicts: {{count}}',
          PART_FAILED: 'failed: {{count}}',
          PART_SKIPPED: 'skipped: {{count}}',
          RULES_REPAIRED: 'Rules repaired: {{count}}',
          RULES_REPAIRED_WITH_PROBLEMS: 'Rules repaired: {{count}}; {{problems}}',
          RULES_SCANNING: 'Rules: checking users {{done}}/{{total}}',
        },
      },
    });
    translate.use('en');
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
    [repair({ phase: 'Scanned', repairableCount: 4 }), 'RULES_TO_REPAIR'],
    [repair({ phase: 'Scanned', skippedCount: 2 }), 'RULES_INCOMPLETE'],
    [repair({ phase: 'Failed' }), 'RULES_FAILED'],
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

  describe('a finished repair', () => {
    const item = (uid: number): RuleRepairItem => ({
      uid,
      after: '',
      before: '',
      humanId: 'u',
      kind: 'MetalGrunt',
      profileNo: 1,
      reason: 'x',
      type: 'invasion',
    });
    const applied = (over: { conflicts?: number; failed?: number; repaired?: number; skipped?: number } = {}) =>
      repair({
        applied: {
          alreadyFine: 0,
          conflicts: Array.from({ length: over.conflicts ?? 0 }, (_, i) => item(i + 100)),
          failed: Array.from({ length: over.failed ?? 0 }, (_, i) => item(i + 1)),
          gone: 0,
          repaired: over.repaired ?? 5,
        },
        finishedAt: '2026-10-22T10:00:00Z',
        phase: 'Applied',
        skippedCount: over.skipped ?? 0,
      });
    const details = (fixture: ReturnType<typeof setup>) =>
      (fixture.nativeElement as HTMLElement).querySelector('[data-test="status-details"]') as HTMLElement;
    const rulesPart = (fixture: ReturnType<typeof setup>) =>
      (fixture.nativeElement as HTMLElement).querySelector('[data-test="part-rules"]')!;

    it('states how many were repaired when everything went', () => {
      const fixture = setup(profile(), applied({ repaired: 5 }));

      expect(text(fixture, 'rules')).toBe('Rules repaired: 5');
      expect(rulesPart(fixture).classList).not.toContain('tone-warn');
      expect(details(fixture).hidden).toBe(true);
    });

    it('warns with the counts when any failed, conflicted or were skipped, and opens the details', () => {
      const fixture = setup(profile(), applied({ conflicts: 1, failed: 2, repaired: 3, skipped: 4 }));

      expect(text(fixture, 'rules')).toContain('Rules repaired: 3; failed: 2, conflicts: 1, skipped: 4');
      expect(rulesPart(fixture).classList).toContain('tone-warn');
      expect(details(fixture).hidden).toBe(false);
    });

    it.each([
      [{ failed: 1 }, 'failed: 1'],
      [{ conflicts: 2 }, 'conflicts: 2'],
      [{ skipped: 3 }, 'skipped: 3'],
    ])('treats each problem on its own as worth a look (%#)', (over, shown) => {
      const fixture = setup(profile(), applied(over));

      expect(text(fixture, 'rules')).toContain(shown);
      expect(details(fixture).hidden).toBe(false);
    });

    it('stays collapsed once the admin closes it, and reopens by hand', () => {
      const fixture = setup(profile(), applied({ failed: 1 }));
      const toggle = (fixture.nativeElement as HTMLElement).querySelector('[data-test="status-details-toggle"]') as HTMLButtonElement;

      toggle.click();
      fixture.detectChanges();
      expect(details(fixture).hidden).toBe(true);

      toggle.click();
      fixture.detectChanges();
      expect(details(fixture).hidden).toBe(false);
    });

    const setStatus = (fixture: ReturnType<typeof setup>, status: RuleRepairStatus) => {
      fixture.debugElement.query(By.directive(RuleRepairCardComponent)).componentInstance.status.set(status);
      fixture.detectChanges();
    };
    const toggleDetails = (fixture: ReturnType<typeof setup>) => {
      ((fixture.nativeElement as HTMLElement).querySelector('[data-test="status-details-toggle"]') as HTMLButtonElement).click();
      fixture.detectChanges();
    };

    it('reopens the details for a later outcome after the admin collapsed an earlier one', () => {
      const fixture = setup(profile(), applied({ failed: 1 }));
      toggleDetails(fixture);
      expect(details(fixture).hidden).toBe(true);

      setStatus(fixture, repair({ finishedAt: '2026-10-22T11:30:00Z', phase: 'Failed', startedAt: '2026-10-22T11:29:00Z' }));

      expect(details(fixture).hidden).toBe(false);
    });

    it('treats two failed runs as two outcomes even when neither says when it finished', () => {
      const fixture = setup(profile(), repair({ error: 'down', phase: 'Failed', startedAt: '2026-10-22T10:00:00Z' }));
      toggleDetails(fixture);
      expect(details(fixture).hidden).toBe(true);

      setStatus(fixture, repair({ error: 'down', phase: 'Failed', startedAt: '2026-10-22T12:00:00Z' }));

      expect(details(fixture).hidden).toBe(false);
    });

    it('keeps a collapsed outcome collapsed while it is still the same outcome', () => {
      const fixture = setup(profile(), repair({ error: 'down', phase: 'Failed', startedAt: '2026-10-22T10:00:00Z' }));
      toggleDetails(fixture);

      setStatus(fixture, repair({ error: 'down', phase: 'Failed', startedAt: '2026-10-22T10:00:00Z' }));

      expect(details(fixture).hidden).toBe(true);
    });

    it('opens the details for a run that failed', () => {
      const fixture = setup(profile(), repair({ finishedAt: '2026-10-22T10:00:00Z', phase: 'Failed' }));

      expect(details(fixture).hidden).toBe(false);
    });
  });

  it('shows progress while a scan runs, without announcing every poll', () => {
    const fixture = setup(profile(), repair({ humansDone: 3, humansTotal: 10, phase: 'Scanning' }));
    const el = fixture.nativeElement as HTMLElement;

    expect(text(fixture, 'rules')).toBe('Rules: checking users 3/10');
    expect(el.querySelector('[data-test="status-line"]')?.getAttribute('role')).toBeNull();
    expect(el.querySelector('[data-test="status-live"]')?.textContent?.trim()).toBe('');
  });

  it('announces the rule state once it settles', () => {
    const fixture = setup(
      profile(),
      repair({ applied: { alreadyFine: 0, conflicts: [], failed: [], gone: 0, repaired: 2 }, phase: 'Applied' }),
    );

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-test="status-live"]')?.textContent?.trim()).toBe(
      'Rules repaired: 2',
    );
  });
});
