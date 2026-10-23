import { inject } from '@angular/core';

import { LinkSide } from '../../core/models';
import { appKey, MoveEdit, otherSide } from '../../core/services/alarm-move.service';
import { I18nService } from '../../core/services/i18n.service';
import { LinkedAccountService } from '../../core/services/linked-account.service';

/**
 * The side an edit moves its alarm to, or null when it stays where it is. Only a person with a link has an
 * `original` (the API stamps `destination` on nobody else's alarms), so an unlinked person's edit can
 * never name one.
 */
export function moveTargetFor(original: LinkSide | null | undefined, chosen: LinkSide | null | undefined): LinkSide | null {
  if (!original || !chosen || original === chosen) return null;
  return chosen;
}

/** What an edit says after it moved its alarm, or null for an ordinary save (which keeps its own message). */
export function moveEditMessage(edit: MoveEdit | null | undefined | void, i18n: I18nService): null | string {
  if (!edit) return null;
  const app = i18n.instant(appKey(edit.destination));
  if (edit.status === 'moved') return i18n.instant('ALARM.MOVED_TO', { app });
  return i18n.instant('ALARM.MOVED_SOURCE_REMAINS', { app, from: i18n.instant(appKey(otherSide(edit.destination))) });
}

/** The sentence an add shows when the side it created on is paused. */
export function pausedCreateMessage(side: LinkSide | null | undefined, i18n: I18nService): string {
  return i18n.instant('DELIVER_TO.PAUSED', { app: i18n.instant(appKey(side ?? 'discord')) });
}

/**
 * What every alarm dialog needs from "Deliver to", in one injectable: whether the field shows, the
 * `destination` to put on a create, and an edit that moves the alarm when the person picked the other app.
 * Call it in a field initialiser.
 */
export function injectDeliverTo() {
  const linked = inject(LinkedAccountService);

  return {
    /** The fields to spread into a create body: `destination` when the field was on screen, nothing otherwise. */
    createFields(chosen: LinkSide | null | undefined): { destination?: LinkSide } {
      return linked.canChoose() && chosen ? { destination: chosen } : {};
    },

    /**
     * The fields to spread into an edit body: `destination` naming the other app when the person picked it
     * (the API then moves the alarm and the service reads how it ended), nothing for any other save.
     */
    moveFields(original: LinkSide | null | undefined, chosen: LinkSide | null | undefined): { destination?: LinkSide } {
      const target = linked.canChoose() ? moveTargetFor(original, chosen) : null;
      return target ? { destination: target } : {};
    },
  };
}
