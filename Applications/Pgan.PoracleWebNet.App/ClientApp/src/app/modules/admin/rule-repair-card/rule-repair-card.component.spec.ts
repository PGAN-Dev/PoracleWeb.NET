import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

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

const status = (over: Partial<RuleRepairStatus> = {}): RuleRepairStatus => ({
  applied: null,
  cannotEdit: [],
  error: null,
  finishedAt: null,
  humansDone: 0,
  humansTotal: 0,
  phase: 'Idle',
  repairable: [],
  skipped: [],
  startedAt: null,
  ...over,
});

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

  it('polls every two seconds while scanning, then stops when the scan is done', () => {
    const fixture = setup(status({ humansDone: 1, humansTotal: 4, phase: 'Scanning' }));
    service.getStatus.mockReturnValue(of(status({ humansDone: 2, humansTotal: 4, phase: 'Scanning' })));
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(service.getStatus).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.status().humansDone).toBe(2);

    service.getStatus.mockReturnValue(of(status({ phase: 'Scanned', repairable: [item()] })));
    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(service.getStatus).toHaveBeenCalledTimes(3);

    jest.advanceTimersByTime(10000);
    expect(service.getStatus).toHaveBeenCalledTimes(3);
  });

  it('polls while applying', () => {
    setup(status({ phase: 'Applying', repairable: [item()] }));
    jest.advanceTimersByTime(4000);
    expect(service.getStatus).toHaveBeenCalledTimes(3);
  });

  it('keeps polling after one poll fails', () => {
    setup(status({ phase: 'Scanning' }));
    service.getStatus.mockReturnValueOnce(throwError(() => new Error('blip')));
    jest.advanceTimersByTime(2000);
    service.getStatus.mockReturnValue(of(status({ phase: 'Scanned' })));
    jest.advanceTimersByTime(2000);
    expect(service.getStatus).toHaveBeenCalledTimes(3);
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
    const repair = button(fixture.nativeElement, 'repair')!;
    expect(repair.disabled).toBe(false);
    repair.click();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(dialog.open.mock.calls[0][1].data.message).toContain('ADMIN.RULE_REPAIR.CONFIRM_MESSAGE');
    expect(service.apply).not.toHaveBeenCalled();
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
        applied: { alreadyFine: 1, conflicts: 2, failed: [item({ reason: 'boom' })], gone: 3, repaired: 4 },
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
});
