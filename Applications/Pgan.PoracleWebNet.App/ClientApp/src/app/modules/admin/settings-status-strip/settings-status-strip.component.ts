import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { RuleRepairStatus } from '../../../core/models';
import { ServerProfileCardComponent } from '../../../shared/components/server-profile-card/server-profile-card.component';
import { RuleRepairCardComponent } from '../rule-repair-card/rule-repair-card.component';

/** How a part of the line reads: plain, worth a look, or broken. Colour only where it means something. */
export type StatusTone = 'error' | 'plain' | 'warn';

export interface StatusPart {
  id: 'poracle' | 'rules' | 'web';
  text: string;
  tone: StatusTone;
}

/**
 * The Versions and Rule repair cards, folded into one line above the settings: "PoracleWeb.NET 2.30 ·
 * PoracleNG 5.4.0 · Rules: nothing to repair  [Details]". The two cards used to open the page at full
 * height, so every setting sat a screen down behind information an admin reads once.
 *
 * The cards stay mounted while collapsed, so the line reads their state rather than asking the server a
 * second time, and a scan started from the details keeps its progress in the line when they are closed.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, RuleRepairCardComponent, ServerProfileCardComponent, TranslatePipe],
  selector: 'app-settings-status-strip',
  standalone: true,
  styleUrl: './settings-status-strip.component.scss',
  templateUrl: './settings-status-strip.component.html',
})
export class SettingsStatusStripComponent {
  /** The outcome the admin collapsed the details on; a later outcome opens them again. */
  private readonly dismissedAttention = signal<null | string>(null);
  private readonly i18n = inject(TranslateService);
  private readonly openedByHand = signal(false);

  private readonly repairCard = viewChild.required(RuleRepairCardComponent);

  private readonly serverCard = viewChild.required(ServerProfileCardComponent);

  /**
   * Identifies a rule-repair outcome that needs a look: a run that failed, or a repair that left rules
   * failed, unrepaired as duplicates, or users unread. While one is showing, the details open on their own,
   * so the card with the lists is on screen; null otherwise.
   */
  readonly attentionKey = computed<null | string>(() => {
    const s = this.repairCard().status();
    // Every run stamps its start, so two runs never share a key even when one ends without a finish time.
    const run = `${s.startedAt ?? ''}/${s.finishedAt ?? ''}`;
    if (s.phase === 'Failed') return `Failed:${run}`;
    if (s.phase === 'Applied' && this.applyProblems(s).length > 0) return `Applied:${run}`;
    return null;
  });

  readonly detailsOpen = computed(() => {
    const key = this.attentionKey();
    return this.openedByHand() || (key !== null && key !== this.dismissedAttention());
  });

  /**
   * What a screen reader is told. Not the whole line: during a run the line changes on every poll, and
   * announcing "checking users 3/40" each time drowns everything else. The rule state is announced when
   * it settles.
   */
  readonly liveText = computed(() => (this.repairCard().busy() ? '' : this.rulesPart().text));

  readonly parts = computed<StatusPart[]>(() => [this.webPart(), this.poraclePart(), this.rulesPart()].filter(p => p !== null));

  toggleDetails(): void {
    if (this.detailsOpen()) {
      this.openedByHand.set(false);
      this.dismissedAttention.set(this.attentionKey());
    } else {
      this.openedByHand.set(true);
    }
  }

  /** "failed: 2", "left alone as duplicates: 1", "users skipped: 3": what a finished repair did not do. */
  private applyProblems(s: RuleRepairStatus): string[] {
    const problems: string[] = [];
    const failed = s.applied?.failed.length ?? 0;
    const conflicts = s.applied?.conflicts.length ?? 0;
    if (failed > 0) problems.push(this.t('ADMIN.STATUS_STRIP.PART_FAILED', { count: failed }));
    if (conflicts > 0) problems.push(this.t('ADMIN.STATUS_STRIP.PART_CONFLICTS', { count: conflicts }));
    if (s.skippedCount > 0) problems.push(this.t('ADMIN.STATUS_STRIP.PART_SKIPPED', { count: s.skippedCount }));
    return problems;
  }

  private poraclePart(): null | StatusPart {
    const card = this.serverCard();
    if (card.serverLoading()) return null;
    const p = card.serverProfile();
    const version = p?.version ?? this.t('ADMIN.SERVER_UNKNOWN');
    const name = this.t('ADMIN.STATUS_STRIP.PORACLE', { version });
    if (!p || !p.reachable) {
      return { id: 'poracle', text: `${name}, ${this.t('ADMIN.STATUS_STRIP.UNREACHABLE')}`, tone: 'warn' };
    }
    if (p.belowMinimum) {
      return {
        id: 'poracle',
        text: `${name}, ${this.t('ADMIN.STATUS_STRIP.TOO_OLD', { minimum: p.minimumSupported })}`,
        tone: 'error',
      };
    }
    if (p.poracleUpdate.state === 'Behind' && p.poracleUpdate.latest) {
      return { id: 'poracle', text: `${name}, ${this.t('ADMIN.STATUS_STRIP.UPDATE', { latest: p.poracleUpdate.latest })}`, tone: 'plain' };
    }
    return { id: 'poracle', text: name, tone: 'plain' };
  }

  private rulesPart(): StatusPart {
    const card = this.repairCard();
    const s = card.status();
    const part = (key: string, tone: StatusTone, params?: Record<string, unknown>): StatusPart => ({
      id: 'rules',
      text: this.t(key, params),
      tone,
    });
    if (card.requestFailed()) return part('ADMIN.STATUS_STRIP.RULES_UNAVAILABLE', 'warn');
    switch (s.phase) {
      case 'Scanning':
        return part('ADMIN.STATUS_STRIP.RULES_SCANNING', 'plain', { done: s.humansDone, total: s.humansTotal });
      case 'Applying':
        return part('ADMIN.STATUS_STRIP.RULES_APPLYING', 'plain', { done: s.humansDone, total: s.humansTotal });
      case 'Failed':
        return part('ADMIN.STATUS_STRIP.RULES_FAILED', 'error');
      case 'Applied': {
        const repaired = s.applied?.repaired ?? 0;
        const problems = this.applyProblems(s);
        return problems.length
          ? part('ADMIN.STATUS_STRIP.RULES_REPAIRED_WITH_PROBLEMS', 'warn', { count: repaired, problems: problems.join(', ') })
          : part('ADMIN.STATUS_STRIP.RULES_REPAIRED', 'plain', { count: repaired });
      }
      case 'Scanned':
        if (s.repairableCount > 0) return part('ADMIN.STATUS_STRIP.RULES_TO_REPAIR', 'warn', { count: s.repairableCount });
        // Users that could not be read may hold rules to repair, so this is never "nothing".
        if (s.skippedCount > 0) return part('ADMIN.STATUS_STRIP.RULES_INCOMPLETE', 'warn');
        return part('ADMIN.STATUS_STRIP.RULES_NOTHING', 'plain');
      default:
        return part('ADMIN.STATUS_STRIP.RULES_NOT_SCANNED', 'plain');
    }
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.i18n.instant(key, params);
  }

  private webPart(): null | StatusPart {
    const card = this.serverCard();
    if (card.serverLoading()) return null;
    const p = card.serverProfile();
    const version = p?.web.version ?? this.t('ADMIN.SERVER_UNKNOWN');
    const name = this.t('ADMIN.STATUS_STRIP.WEB', { version });
    if (p?.webUpdate.state === 'Behind' && p.webUpdate.latest) {
      return { id: 'web', text: `${name}, ${this.t('ADMIN.STATUS_STRIP.UPDATE', { latest: p.webUpdate.latest })}`, tone: 'plain' };
    }
    return { id: 'web', text: name, tone: 'plain' };
  }
}
