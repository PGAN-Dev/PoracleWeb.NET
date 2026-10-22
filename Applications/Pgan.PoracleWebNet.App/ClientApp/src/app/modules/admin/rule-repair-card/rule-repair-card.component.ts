import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { EMPTY, catchError, interval, switchMap } from 'rxjs';

import { RuleRepairItem, RuleRepairKind, RuleRepairStatus } from '../../../core/models';
import { RuleRepairService } from '../../../core/services/rule-repair.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';

export interface RuleRepairGroup {
  items: RuleRepairItem[];
  kind: RuleRepairKind;
  labelKey: string;
  shown: RuleRepairItem[];
}

const IDLE: RuleRepairStatus = {
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

/**
 * Scans every user's stored rules for ones PoracleNG 5.4.0 reads differently, and repairs them on an
 * admin's confirmation. The job runs on the server; this card starts it and polls for progress, and only
 * while a run is going.
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

  readonly busy = computed(() => this.status().phase === 'Scanning' || this.status().phase === 'Applying');

  /** Repair is offered after a scan only; once applied the list is the old scan's, so the admin rescans. */
  readonly canRepair = computed(() => this.status().phase === 'Scanned' && this.status().repairable.length > 0);

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

  constructor() {
    this.service.getStatus().subscribe({
      error: () => this.requestFailed.set(true),
      next: s => this.status.set(s),
    });

    // The effect re-runs when `busy` flips, and onCleanup tears the timer down each time and when the
    // component is destroyed, so nothing polls while the card is idle or gone.
    effect(onCleanup => {
      if (!this.busy()) {
        return;
      }
      const sub = interval(POLL_MS)
        .pipe(switchMap(() => this.service.getStatus().pipe(catchError(() => EMPTY))))
        .subscribe(s => this.status.set(s));
      onCleanup(() => sub.unsubscribe());
    });
  }

  repair(): void {
    const count = this.status().repairable.length;
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
