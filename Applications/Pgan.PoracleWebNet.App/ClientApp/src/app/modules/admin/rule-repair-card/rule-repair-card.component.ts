import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { EMPTY, catchError, exhaustMap, interval, of, switchMap } from 'rxjs';

import { RuleRepairItem, RuleRepairKind, RuleRepairSkip, RuleRepairStatus } from '../../../core/models';
import { RuleRepairService } from '../../../core/services/rule-repair.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';

export interface RuleRepairGroup {
  items: RuleRepairItem[];
  kind: RuleRepairKind;
  labelKey: string;
  shown: RuleRepairItem[];
}

/** Rules that cannot be edited, sharing one reason. */
export interface RuleRepairReasonGroup {
  reason: string;
  shown: RuleRepairItem[];
  total: number;
}

/** A list cut to {@link MAX_ROWS_PER_GROUP} rows, with how long it was. */
export interface CappedList<T> {
  shown: T[];
  total: number;
}

const IDLE: RuleRepairStatus = {
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
};

const KIND_ORDER: RuleRepairKind[] = ['MetalGrunt', 'RaidPokemonZero', 'RaidAnyBossLevel9000', 'MaxBattleAnyBossLevel9000'];

const KIND_LABELS: Record<RuleRepairKind, string> = {
  RaidAnyBossLevel9000: 'ADMIN.RULE_REPAIR.KIND_RAID_ANY_BOSS',
  RaidPokemonZero: 'ADMIN.RULE_REPAIR.KIND_RAID_POKEMON_ZERO',
  CannotEdit: 'ADMIN.RULE_REPAIR.KIND_CANNOT_EDIT',
  MaxBattleAnyBossLevel9000: 'ADMIN.RULE_REPAIR.KIND_MAXBATTLE_ANY_BOSS',
  MetalGrunt: 'ADMIN.RULE_REPAIR.KIND_METAL_GRUNT',
};

/** A list can run to thousands of rows; the table shows this many per group and says how many it hid. */
const MAX_ROWS_PER_GROUP = 100;

const POLL_MS = 2000;

const isBusy = (s: RuleRepairStatus) => s.phase === 'Scanning' || s.phase === 'Applying';

const cap = <T>(items: T[]): CappedList<T> => ({ shown: items.slice(0, MAX_ROWS_PER_GROUP), total: items.length });

/**
 * Scans every user's stored rules for ones PoracleNG 5.4.0 reads differently, and repairs them on an
 * admin's confirmation. The job runs on the server; this card starts it and polls for progress, and only
 * while a run is going. The poll asks for a summary (counts, no lists), and the full status is read once,
 * when the run has ended.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  selector: 'app-rule-repair-card',
  standalone: true,
  styleUrl: './rule-repair-card.component.scss',
  templateUrl: './rule-repair-card.component.html',
})
export class RuleRepairCardComponent {
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(TranslateService);
  private readonly service = inject(RuleRepairService);

  readonly status = signal<RuleRepairStatus>(IDLE);

  readonly busy = computed(() => isBusy(this.status()));

  /** Grouped by reason, largest first, so one common reason can't push a rare one off the page. */
  readonly cannotEditGroups = computed<RuleRepairReasonGroup[]>(() => {
    const byReason = new Map<string, RuleRepairItem[]>();
    for (const item of this.status().cannotEdit) {
      const reason = item.reason ?? '';
      const list = byReason.get(reason);
      if (list) {
        list.push(item);
      } else {
        byReason.set(reason, [item]);
      }
    }
    return [...byReason.entries()]
      .map(([reason, items]) => ({ reason, ...cap(items) }))
      .sort((a, b) => b.total - a.total || a.reason.localeCompare(b.reason));
  });

  /** Repair is offered after a scan only; once applied the list is the old scan's, so the admin rescans. */
  readonly canRepair = computed(() => this.status().phase === 'Scanned' && this.status().repairableCount > 0);

  readonly conflicts = computed<CappedList<RuleRepairItem>>(() => cap(this.status().applied?.conflicts ?? []));

  readonly failed = computed<CappedList<RuleRepairItem>>(() => cap(this.status().applied?.failed ?? []));

  readonly groups = computed<RuleRepairGroup[]>(() => {
    const repairable = this.status().repairable;
    return KIND_ORDER.map(kind => {
      const items = repairable.filter(i => i.kind === kind);
      return { items, kind, labelKey: KIND_LABELS[kind], shown: items.slice(0, MAX_ROWS_PER_GROUP) };
    }).filter(g => g.items.length > 0);
  });

  readonly maxRows = MAX_ROWS_PER_GROUP;

  readonly progress = computed(() => {
    const s = this.status();
    return s.humansTotal > 0 ? Math.round((s.humansDone / s.humansTotal) * 100) : 0;
  });

  readonly requestFailed = signal(false);

  readonly skipped = computed<CappedList<RuleRepairSkip>>(() => cap(this.status().skipped));

  /** An apply that failed leaves the scan's list behind it, which may no longer be true. */
  readonly staleAfterFailedApply = computed(() => this.status().phase === 'Failed' && this.status().repairableCount > 0);

  constructor() {
    this.service.getStatus().subscribe({
      error: () => this.requestFailed.set(true),
      next: s => this.status.set(s),
    });

    // The effect re-runs when `busy` flips, and onCleanup tears the timer down each time and when the
    // component is destroyed, so nothing polls while the card is idle or gone. A summary that says the run
    // has ended is not shown: the full status is read in its place, and if that read fails the card stays
    // busy and the next tick tries again.
    effect(onCleanup => {
      if (!this.busy()) {
        return;
      }
      const sub = interval(POLL_MS)
        .pipe(
          // exhaustMap, not switchMap: once the summary says the run ended, the full read must not be
          // cancelled and restarted by every later tick, or a slow read never lands.
          exhaustMap(() =>
            this.service.getStatus(true).pipe(
              switchMap(s => (isBusy(s) ? of(s) : this.service.getStatus())),
              catchError(() => EMPTY),
            ),
          ),
        )
        .subscribe(s => this.status.set(s));
      onCleanup(() => sub.unsubscribe());
    });
  }

  repair(): void {
    const count = this.status().repairableCount;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.RULE_REPAIR.CONFIRM_BUTTON', { count }),
        message: this.i18n.instant('ADMIN.RULE_REPAIR.CONFIRM_MESSAGE', { count }),
        title: this.i18n.instant('ADMIN.RULE_REPAIR.CONFIRM_TITLE'),
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(result => {
      if (result) {
        this.start(this.service.apply());
      }
    });
  }

  scan(): void {
    this.start(this.service.scan());
  }

  private start(call: ReturnType<RuleRepairService['scan']>): void {
    this.requestFailed.set(false);
    call.subscribe({
      error: () => this.requestFailed.set(true),
      next: s => this.status.set(s),
    });
  }
}
