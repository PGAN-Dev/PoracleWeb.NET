import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';

import { serverReason } from './server-reason';
import { LinkSide } from '../../core/models';
import { AlarmMoveService, failedOutcomes, MoveOutcome, MoveType, summariseMoves } from '../../core/services/alarm-move.service';
import { I18nService } from '../../core/services/i18n.service';

/** What a list hands over: the three things a move needs besides what to move. */
export interface BulkMoveDeps {
  i18n: I18nService;
  moves: AlarmMoveService;
  snackBar: MatSnackBar;
}

/** The selected uids of one tracking type. A list with one type passes one group; raids pass raid and egg. */
export interface BulkMoveGroup {
  /** For the invasion list: the kind of the cards, so a uid shared with the other kind is not moved with them. */
  kind?: 'event' | 'invasion';
  type: MoveType;
  uids: readonly number[];
}

/**
 * The one bulk move all ten alarm lists run: send each group, report every outcome in one line, then call
 * `afterMove` (clear the selection, reload) whatever happened, because part of a long selection may have
 * moved before a later request failed. One group failing must not hide what another already did. Shared so
 * the ten lists can't drift apart.
 */
export async function runBulkMove(
  deps: BulkMoveDeps,
  groups: readonly BulkMoveGroup[],
  destination: LinkSide,
  afterMove: () => void,
): Promise<void> {
  const { i18n, moves, snackBar } = deps;
  try {
    const results: MoveOutcome[] = [];
    for (const { uids, kind, type } of groups) {
      if (uids.length === 0) continue;
      try {
        results.push(...(await firstValueFrom(moves.moveMany(type, uids, destination, kind))).results);
      } catch (err) {
        results.push(...failedOutcomes(uids, err));
      }
    }
    const text = summariseMoves(results, destination, i18n);
    if (text) snackBar.open(text, i18n.instant('COMMON.OK'), { duration: 8000 });
  } catch (err) {
    snackBar.open(serverReason(err) ?? i18n.instant('ALARM.MOVE_FAILED_ALL'), i18n.instant('COMMON.OK'), { duration: 6000 });
  }
  afterMove();
}
