import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { Subject, map, of, throwError, timer } from 'rxjs';

import { RuleRepairCardComponent } from './rule-repair-card.component';
import { RuleRepairItem, RuleRepairStatus } from '../../../core/models';
import { RuleRepairService } from '../../../core/services/rule-repair.service';

const item = (over: Partial<RuleRepairItem> = {}): RuleRepairItem => ({
  uid: 10,
  after: 'grunt_type steel',
  before: 'grunt_type metal',
  humanId: 'alice',
  kind: 'MetalGrunt',
  profileNo: 1,
  reason: null,
  type: 'invasion',
  ...over,
});

/** A full status: the counts follow the lists unless a test says otherwise, as the server's do. */
const status = (over: Partial<RuleRepairStatus> = {}): RuleRepairStatus => {
  const base: RuleRepairStatus = {
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
  };
  return {
    ...base,
    cannotEditCount: over.cannotEditCount ?? base.cannotEdit.length,
    repairableCount: over.repairableCount ?? base.repairable.length,
    skippedCount: over.skippedCount ?? base.skipped.length,
  };
};

const many = (n: number, over: (i: number) => Partial<RuleRepairItem>): RuleRepairItem[] =>
  Array.from({ length: n }, (_, i) => item({ uid: i + 1, ...over(i) }));

describe('RuleRepairCardComponent', () => {
  let service: { apply: jest.Mock; getStatus: jest.Mock; scan: jest.Mock };
  let dialog: { open: jest.Mock };

  function setup(initial: RuleRepairStatus, confirm = true) {
    service = {
      apply: jest.fn().mockReturnValue(of(status({ phase: 'Applying' }))),
      getStatus: jest.fn().mockReturnValue(of(initial)),
      scan: jest.fn().mockReturnValue(of(status({ phase: 'Scanning' }))),
    };
    dialog = { open: jest.fn().mockReturnValue({ afterClosed: () => of(confirm) }) };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RuleRepairService, useValue: service },
        { provide: MatDialog, useValue: dialog },
      ],
      imports: [RuleRepairCardComponent],
    });
    const fixture = TestBed.createComponent(RuleRepairCardComponent);
    fixture.detectChanges();
    return fixture;
  }

  const button = (el: HTMLElement, id: string) => el.querySelector<HTMLButtonElement>(`[data-test="${id}"]`);

  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('reads the status once on load and does not poll while idle', () => {
    setup(status());
    jest.advanceTimersByTime(10000);
    expect(service.getStatus).toHaveBeenCalledTimes(1);
  });

  it('does not poll once a scan has finished', () => {
    setup(status({ phase: 'Scanned', repairable: [item()] }));
    jest.advanceTimersByTime(10000);
    expect(service.getStatus).toHaveBeenCalledTimes(1);
  });

  it('polls a summary every two seconds while scanning, then reads the full status once when the scan is done', () => {
    const fixture = setup(status({ humansDone: 1, humansTotal: 4, phase: 'Scanning' }));
    service.getStatus.mockReturnValue(of(status({ humansDone: 2, humansTotal: 4, phase: 'Scanning' })));
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(service.getStatus).toHaveBeenCalledTimes(2);
    expect(service.getStatus).toHaveBeenLastCalledWith(true);
    expect(fixture.componentInstance.status().humansDone).toBe(2);

    // The summary says the scan is done, with the lists left out; the full status follows it.
    service.getStatus.mockImplementation((summary?: boolean) =>
      of(summary ? status({ phase: 'Scanned', repairableCount: 1 }) : status({ phase: 'Scanned', repairable: [item()] })),
    );
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(service.getStatus).toHaveBeenCalledTimes(4);
    expect(service.getStatus.mock.calls[2]).toEqual([true]);
    expect(service.getStatus.mock.calls[3]).toEqual([]);
    expect(fixture.componentInstance.status().repairable).toHaveLength(1);

    jest.advanceTimersByTime(10000);
    expect(service.getStatus).toHaveBeenCalledTimes(4);
  });

  it('polls summaries while applying', () => {
    setup(status({ phase: 'Applying', repairable: [item()] }));
    jest.advanceTimersByTime(4000);
    expect(service.getStatus).toHaveBeenCalledTimes(3);
    expect(service.getStatus.mock.calls.slice(1)).toEqual([[true], [true]]);
  });

  it('keeps polling after one poll fails', () => {
    setup(status({ phase: 'Scanning' }));
    service.getStatus.mockReturnValueOnce(throwError(() => new Error('blip')));
    jest.advanceTimersByTime(2000);
    service.getStatus.mockReturnValue(of(status({ phase: 'Scanned' })));
    jest.advanceTimersByTime(2000);
    // The failed poll, then a summary saying done, then the full read.
    expect(service.getStatus).toHaveBeenCalledTimes(4);
  });

  it('keeps polling when the full read after a finished run fails, and tries it again', () => {
    const fixture = setup(status({ phase: 'Scanning' }));
    let fullReads = 0;
    service.getStatus.mockImplementation((summary?: boolean) => {
      if (summary) {
        return of(status({ phase: 'Scanned', repairableCount: 1 }));
      }
      fullReads++;
      return fullReads === 1 ? throwError(() => new Error('blip')) : of(status({ phase: 'Scanned', repairable: [item()] }));
    });
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(fixture.componentInstance.busy()).toBe(true);
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(fullReads).toBe(2);
    expect(fixture.componentInstance.status().repairable).toHaveLength(1);
  });

  it('reads the finished report once when the full read outlasts the poll interval', () => {
    const fixture = setup(status({ phase: 'Scanning' }));
    service.getStatus.mockClear();
    service.getStatus.mockImplementation((summary?: boolean) =>
      summary
        ? of(status({ phase: 'Scanned', repairableCount: 1 }))
        : timer(5000).pipe(map(() => status({ phase: 'Scanned', repairable: [item()] }))),
    );
    jest.advanceTimersByTime(2000);
    // Ticks land at 4s and 6s while the full read is still in flight.
    jest.advanceTimersByTime(5000);
    fixture.detectChanges();
    const fullReads = () => service.getStatus.mock.calls.filter(c => !c[0]).length;
    expect(fullReads()).toBe(1);
    expect(fixture.componentInstance.busy()).toBe(false);
    expect(fixture.componentInstance.status().repairable).toHaveLength(1);

    const callsAfter = service.getStatus.mock.calls.length;
    jest.advanceTimersByTime(20000);
    expect(service.getStatus.mock.calls.length).toBe(callsAfter);
  });

  it('stops polling when destroyed', () => {
    const fixture = setup(status({ phase: 'Scanning' }));
    jest.advanceTimersByTime(2000);
    expect(service.getStatus).toHaveBeenCalledTimes(2);
    fixture.destroy();
    jest.advanceTimersByTime(10000);
    expect(service.getStatus).toHaveBeenCalledTimes(2);
  });

  it('starts a scan from the Scan button and shows progress', () => {
    const fixture = setup(status());
    button(fixture.nativeElement, 'scan')!.click();
    fixture.detectChanges();
    expect(service.scan).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.status().phase).toBe('Scanning');
  });

  it('disables Scan and hides Repair while a scan is going', () => {
    const fixture = setup(status({ phase: 'Scanning', repairable: [item()] }));
    expect(button(fixture.nativeElement, 'scan')!.disabled).toBe(true);
    expect(button(fixture.nativeElement, 'repair')).toBeNull();
  });

  it('disables Scan while applying', () => {
    const fixture = setup(status({ phase: 'Applying', repairable: [item()] }));
    expect(button(fixture.nativeElement, 'scan')!.disabled).toBe(true);
  });

  it('offers Repair after a scan with repairable rules, and asks first naming the count', () => {
    const fixture = setup(status({ phase: 'Scanned', repairable: [item(), item({ uid: 11 })] }), false);
    const instant = jest.spyOn(TestBed.inject(TranslateService), 'instant');
    const repair = button(fixture.nativeElement, 'repair')!;
    expect(repair.disabled).toBe(false);
    repair.click();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(dialog.open.mock.calls[0][1].data.message).toContain('ADMIN.RULE_REPAIR.CONFIRM_MESSAGE');
    expect(instant).toHaveBeenCalledWith('ADMIN.RULE_REPAIR.CONFIRM_MESSAGE', { count: 2 });
    expect(instant).toHaveBeenCalledWith('ADMIN.RULE_REPAIR.CONFIRM_BUTTON', { count: 2 });
    expect(service.apply).not.toHaveBeenCalled();
  });

  it('opens one confirm dialog for a double-click on Repair', () => {
    const closed = new Subject<boolean>();
    const fixture = setup(status({ phase: 'Scanned', repairable: [item()] }));
    dialog.open.mockReturnValue({ afterClosed: () => closed });
    const repair = button(fixture.nativeElement, 'repair')!;
    repair.click();
    repair.click();
    expect(dialog.open).toHaveBeenCalledTimes(1);

    closed.next(false);
    closed.complete();
    repair.click();
    expect(dialog.open).toHaveBeenCalledTimes(2);
  });

  it('offers Repair again once an apply has answered', () => {
    const fixture = setup(status({ phase: 'Scanned', repairable: [item()] }));
    service.apply.mockReturnValue(of(status({ phase: 'Scanned', repairable: [item()] })));
    const repair = button(fixture.nativeElement, 'repair')!;
    repair.click();
    repair.click();
    expect(service.apply).toHaveBeenCalledTimes(2);
  });

  it('offers Repair again after an apply fails', () => {
    const fixture = setup(status({ phase: 'Scanned', repairable: [item()] }));
    service.apply.mockReturnValue(throwError(() => new Error('down')));
    const repair = button(fixture.nativeElement, 'repair')!;
    repair.click();
    expect(fixture.componentInstance.requestFailed()).toBe(true);
    repair.click();
    expect(service.apply).toHaveBeenCalledTimes(2);
  });

  it('sends one apply for a double-click while the request is in flight', () => {
    const inFlight = new Subject<RuleRepairStatus>();
    const fixture = setup(status({ phase: 'Scanned', repairable: [item()] }));
    service.apply.mockReturnValue(inFlight);
    const repair = button(fixture.nativeElement, 'repair')!;
    repair.click();
    repair.click();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(service.apply).toHaveBeenCalledTimes(1);
  });

  it('applies once the admin confirms', () => {
    const fixture = setup(status({ phase: 'Scanned', repairable: [item()] }), true);
    button(fixture.nativeElement, 'repair')!.click();
    fixture.detectChanges();
    expect(service.apply).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.status().phase).toBe('Applying');
  });

  it('shows no Repair button when the scan found nothing to repair', () => {
    const fixture = setup(status({ phase: 'Scanned' }));
    expect(button(fixture.nativeElement, 'repair')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('ADMIN.RULE_REPAIR.NOTHING');
  });

  it('never says nothing needs repairing when users were skipped', () => {
    const fixture = setup(status({ phase: 'Scanned', skipped: [{ humanId: 'bob', reason: 'PoracleNG answered 502' }] }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="nothing"]')).toBeNull();
    expect(el.querySelector('[data-test="nothing-readable"]')).not.toBeNull();
    expect(el.textContent).toContain('ADMIN.RULE_REPAIR.NOTHING_READABLE');
  });

  it('opens the skipped list by default', () => {
    const fixture = setup(status({ phase: 'Scanned', skipped: [{ humanId: 'bob', reason: 'PoracleNG answered 502' }] }));
    expect((fixture.nativeElement as HTMLElement).querySelector('details')!.open).toBe(true);
  });

  it('caps the skipped list and says how many it hid', () => {
    const skipped = Array.from({ length: 150 }, (_, i) => ({ humanId: `h${i}`, reason: 'PoracleNG answered 500' }));
    const fixture = setup(status({ phase: 'Scanned', skipped }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('details li')).toHaveLength(100);
    expect(el.querySelector('details')!.textContent).toContain('ADMIN.RULE_REPAIR.SHOWING');
  });

  it('groups the rules that cannot be edited by reason, with a count each, and caps each group', () => {
    const cannotEdit = [
      ...many(130, () => ({ after: null, kind: 'CannotEdit', reason: 'Level 9 is not a raid tier.', type: 'egg' })),
      ...many(2, i => ({
        uid: 500 + i,
        after: null,
        kind: 'CannotEdit',
        reason: 'Repairing it would duplicate another rule.',
        type: 'raid',
      })),
    ];
    const fixture = setup(status({ cannotEdit, phase: 'Scanned' }));
    const groups = fixture.componentInstance.cannotEditGroups();
    expect(groups.map(g => [g.reason, g.total, g.shown.length])).toEqual([
      ['Level 9 is not a raid tier.', 130, 100],
      ['Repairing it would duplicate another rule.', 2, 2],
    ]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-test="cannot-edit"] li')).toHaveLength(102);
    expect(el.querySelector('[data-test="cannot-edit"]')!.textContent).toContain('ADMIN.RULE_REPAIR.SHOWING');
  });

  it('lists the conflicts and failures of an apply, capped', () => {
    const fixture = setup(
      status({
        applied: {
          alreadyFine: 0,
          conflicts: many(120, () => ({ reason: "Repairing it would duplicate another of this user's rules." })),
          failed: [item({ reason: 'boom' })],
          gone: 0,
          repaired: 0,
        },
        phase: 'Applied',
      }),
    );
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-test="conflicts"] li')).toHaveLength(100);
    expect(el.querySelector('[data-test="conflicts"]')!.textContent).toContain('duplicate another');
    expect(el.textContent).toContain('ADMIN.RULE_REPAIR.SHOWING');
  });

  it('after a failed apply shows a scan-again hint, not the stale list', () => {
    const fixture = setup(status({ error: 'PoracleNG could not be reached', phase: 'Failed', repairable: [item()] }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('ADMIN.RULE_REPAIR.SCAN_AGAIN_HINT');
    expect(el.querySelector('[data-test="run-error"]')!.textContent).toContain('PoracleNG could not be reached');
    expect(el.textContent).not.toContain('grunt_type metal');
    expect(el.querySelector('table')).toBeNull();
    expect(button(el, 'repair')).toBeNull();
  });

  it('after a failed scan shows no scan-again hint (there is no stale list)', () => {
    const fixture = setup(status({ error: 'No user could be read', phase: 'Failed' }));
    expect(fixture.nativeElement.textContent).not.toContain('ADMIN.RULE_REPAIR.SCAN_AGAIN_HINT');
  });

  it('groups repairable rules by kind and shows before and after', () => {
    const fixture = setup(
      status({
        phase: 'Scanned',
        repairable: [
          item(),
          item({ uid: 30, after: 'any boss, any tier', before: 'pokemon 9000, level 9000', kind: 'RaidAnyBossLevel9000', type: 'raid' }),
        ],
      }),
    );
    const groups = fixture.componentInstance.groups();
    expect(groups.map(g => g.kind)).toEqual(['MetalGrunt', 'RaidAnyBossLevel9000']);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('grunt_type metal');
    expect(text).toContain('grunt_type steel');
    expect(text).toContain('ADMIN.RULE_REPAIR.KIND_METAL_GRUNT');
    expect(text).toContain('ADMIN.RULE_REPAIR.KIND_RAID_ANY_BOSS');
  });

  it('lists the rules that cannot be edited with their reasons', () => {
    const fixture = setup(
      status({
        cannotEdit: [item({ after: null, kind: 'CannotEdit', reason: 'Level 9 is not a raid tier.', type: 'egg' })],
        phase: 'Scanned',
      }),
    );
    expect(fixture.nativeElement.textContent).toContain('Level 9 is not a raid tier.');
  });

  it('shows skipped users as a count with the list inside', () => {
    const fixture = setup(status({ phase: 'Scanned', skipped: [{ humanId: 'gone', reason: 'The account no longer exists.' }] }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('details')).not.toBeNull();
    expect(el.textContent).toContain('The account no longer exists.');
  });

  it('shows the apply outcome', () => {
    const fixture = setup(
      status({
        applied: {
          alreadyFine: 1,
          conflicts: [item({ uid: 20 }), item({ uid: 21 })],
          failed: [item({ reason: 'boom' })],
          gone: 3,
          repaired: 4,
        },
        phase: 'Applied',
        repairable: [item()],
      }),
    );
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ADMIN.RULE_REPAIR.RESULT_REPAIRED');
    expect(text).toContain('boom');
    // A finished apply leaves the old list stale, so Repair is not offered again until a rescan.
    expect(button(fixture.nativeElement, 'repair')).toBeNull();
    expect(button(fixture.nativeElement, 'scan')!.disabled).toBe(false);
  });

  it('shows a failed run with its error', () => {
    const fixture = setup(status({ error: 'PoracleNG could not be reached', phase: 'Failed' }));
    expect(fixture.nativeElement.textContent).toContain('PoracleNG could not be reached');
    expect(button(fixture.nativeElement, 'scan')!.disabled).toBe(false);
  });

  it('says so when a scan cannot be started', () => {
    const fixture = setup(status());
    service.scan.mockReturnValue(throwError(() => new Error('down')));
    button(fixture.nativeElement, 'scan')!.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('ADMIN.RULE_REPAIR.REQUEST_FAILED');
    expect(button(fixture.nativeElement, 'scan')!.disabled).toBe(false);
  });
  describe('when the last run finished', () => {
    const finished = '2026-10-22T12:00:00Z';

    const withEnglish = (fixture: ReturnType<typeof setup>) => {
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('en', {
        ADMIN: { RULE_REPAIR: { REPAIRED_AGO: 'Repaired {{when}}', SCANNED_AGO: 'Scanned {{when}}' } },
      });
      translate.use('en');
      fixture.componentInstance.refreshClock();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('[data-test="last-run"]')?.textContent?.trim();
    };

    it('says how long ago the scan ran, so a remembered result does not read as fresh', () => {
      jest.setSystemTime(new Date('2026-10-22T12:03:00Z'));
      const fixture = setup(status({ finishedAt: finished, phase: 'Scanned' }));

      expect(withEnglish(fixture)).toBe('Scanned 3 minutes ago');
    });

    it('says how long ago a repair ran', () => {
      jest.setSystemTime(new Date('2026-10-22T14:00:00Z'));
      const fixture = setup(
        status({ applied: { alreadyFine: 0, conflicts: [], failed: [], gone: 0, repaired: 1 }, finishedAt: finished, phase: 'Applied' }),
      );

      expect(withEnglish(fixture)).toBe('Repaired 2 hours ago');
    });

    it('keeps counting while the page stays open', () => {
      jest.setSystemTime(new Date('2026-10-22T12:00:10Z'));
      const fixture = setup(status({ finishedAt: finished, phase: 'Scanned' }));
      expect(withEnglish(fixture)).toBe('Scanned now');

      jest.advanceTimersByTime(5 * 60 * 1000);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-test="last-run"]').textContent.trim()).toBe('Scanned 5 minutes ago');
    });

    it('follows a language switch without waiting for the next tick', () => {
      jest.setSystemTime(new Date('2026-10-22T12:03:00Z'));
      const fixture = setup(status({ finishedAt: finished, phase: 'Scanned' }));
      withEnglish(fixture);

      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('de', { ADMIN: { RULE_REPAIR: { SCANNED_AGO: 'Geprüft {{when}}' } } });
      translate.use('de');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[data-test="last-run"]').textContent.trim()).toBe('Geprüft vor 3 Minuten');
    });

    it('says nothing before any run, or while one is going', () => {
      expect(setup(status()).nativeElement.querySelector('[data-test="last-run"]')).toBeNull();
      expect(setup(status({ finishedAt: finished, phase: 'Scanning' })).nativeElement.querySelector('[data-test="last-run"]')).toBeNull();
    });
  });
});
