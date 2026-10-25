import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, concatMap, from, map, Observable, of, reduce, throwError } from 'rxjs';

import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { markMoveFailure } from '../../shared/utils/move-failure';
import { serverReason } from '../../shared/utils/server-reason';
import { WORDS_OWN_502 } from '../interceptors/error.interceptor';
import { LinkSide } from '../models';

/** The API refuses more than this many uids in one `POST /api/alarm-move`. */
export const MAX_MOVE_UIDS = 500;

export type MoveStatus = 'alreadyThere' | 'conflict' | 'failed' | 'moved' | 'refused' | 'sourceRemains';

export interface MoveOutcome {
  existingUid?: null | number;
  newUid?: null | number;
  /** The server's English sentence, kept for logs. Nothing on screen shows it: `reasonCode` is translated instead. */
  reason?: null | string;
  /** The app a reason is about, when it names one. */
  reasonApp?: null | string;
  /** Why it did not move, as `AlarmMover`'s `MoveReasonCodes` names it (`place_missing`, `different_profiles`, ...). */
  reasonCode?: null | string;
  /** The saved place a `place_missing` refusal names. */
  reasonPlace?: null | string;
  status: MoveStatus;
  uid: number;
}

/** The refusal codes the SPA has words for; anything else is worded with the generic refusal. */
const REASON_KEYS: Record<string, string> = {
  invalid: 'ALARM.MOVE_REASON_INVALID',
  area_not_allowed: 'ALARM.MOVE_REASON_AREA_NOT_ALLOWED',
  different_profiles: 'ALARM.MOVE_REASON_DIFFERENT_PROFILES',
  failed: 'ALARM.MOVE_REASON_FAILED',
  not_found: 'ALARM.MOVE_REASON_NOT_FOUND',
  not_linked: 'ALARM.MOVE_REASON_NOT_LINKED',
  not_set_up: 'ALARM.MOVE_REASON_NOT_SET_UP',
  not_yours: 'ALARM.MOVE_REASON_NOT_YOURS',
  place_missing: 'ALARM.MOVE_REASON_PLACE_MISSING',
};

interface ReasonParts {
  reasonApp?: null | string;
  reasonCode?: null | string;
  reasonPlace?: null | string;
}

/**
 * The translated sentence for a refusal code, or the generic refusal for a code this build doesn't know.
 * `fallbackApp` names the app when the server didn't (a refusal is almost always about the target).
 */
function reasonText(parts: ReasonParts, fallbackApp: LinkSide, i18n: I18nService): string {
  const key = parts.reasonCode ? REASON_KEYS[parts.reasonCode] : undefined;
  if (!key) return i18n.instant('ALARM.MOVE_REFUSED_NO_REASON');
  const side: LinkSide = parts.reasonApp === 'discord' || parts.reasonApp === 'telegram' ? parts.reasonApp : fallbackApp;
  return i18n.instant(key, { app: i18n.instant(appKey(side)), place: parts.reasonPlace ?? '' });
}

/**
 * What an edit dialog shows when the API refused to move its alarm: the translated sentence for the body's
 * `code`, or null when the error carries none (validation, an outage), so the caller words it as before.
 */
export function moveRefusalText(err: unknown, i18n: I18nService, target: LinkSide = 'discord'): null | string {
  const body = (err as { error?: { app?: unknown; code?: unknown; place?: unknown } } | null | undefined)?.error;
  if (!body || typeof body.code !== 'string' || !REASON_KEYS[body.code]) return null;
  return reasonText(
    {
      reasonApp: typeof body.app === 'string' ? body.app : null,
      reasonCode: body.code,
      reasonPlace: typeof body.place === 'string' ? body.place : null,
    },
    target,
    i18n,
  );
}

export interface AlarmMoveResponse {
  results: MoveOutcome[];
}

/** What a single edit that moved its alarm reports back. */
export interface MoveEdit {
  destination: LinkSide;
  status: 'moved' | 'source_remains';
}

/**
 * PoracleNG's tracking type names, as `POST /api/alarm-move` takes them. Eggs share the raid dialogs and
 * lists but are their own type; Pokéstop events are `incident`.
 */
export type MoveType = 'egg' | 'fort' | 'gym' | 'incident' | 'invasion' | 'lure' | 'maxbattle' | 'nest' | 'pokemon' | 'quest' | 'raid';

/** The API's enum can arrive as `SourceRemains` or `sourceRemains`; compare without case. */
function statusOf(raw: unknown): MoveStatus {
  const key = String(raw ?? '')
    .toLowerCase()
    .replace(/[_\s]/g, '');
  switch (key) {
    case 'moved':
      return 'moved';
    case 'conflict':
      return 'conflict';
    case 'sourceremains':
      return 'sourceRemains';
    case 'refused':
      return 'refused';
    case 'alreadythere':
      return 'alreadyThere';
    default:
      return 'failed';
  }
}

/**
 * What a request that failed outright means for each uid it carried: a refusal (4xx) changed nothing, so
 * it is reported as refused, carrying the body's `code` when it sent one (worded by that code, or generically);
 * anything else may or may not have written, so it is reported as failed, not as not moved.
 */
export function failedOutcomes(uids: readonly number[], err: unknown): MoveOutcome[] {
  const status = (err as { status?: unknown } | null)?.status;
  if (typeof status === 'number' && status < 500 && status > 0) {
    const reason = serverReason(err);
    const code = (err as { error?: { code?: unknown } } | null)?.error?.code;
    return uids.map(uid => ({ uid, reason, ...(typeof code === 'string' ? { reasonCode: code } : {}), status: 'refused' as const }));
  }
  return uids.map(uid => ({ uid, status: 'failed' as const }));
}

/**
 * One summary for a bulk move, per outcome: how many moved and how many were already there (both are what the
 * person asked for), then what did not and why. The copy for a move whose source could not be deleted names both
 * apps, because the alarm now exists on each and the person has to remove one by hand. A refusal is worded from
 * its code, one line per code; the server's English sentence never reaches the screen.
 */
export function summariseMoves(results: readonly MoveOutcome[], target: LinkSide, i18n: I18nService): string {
  const app = i18n.instant(appKey(target));
  const fromApp = i18n.instant(appKey(otherSide(target)));
  const count = (status: MoveStatus) => results.filter(r => statusOf(r.status) === status).length;
  const parts: string[] = [];

  const moved = count('moved');
  if (moved > 0) parts.push(i18n.plural('ALARM.MOVE_DONE', moved, { app }));
  const alreadyThere = count('alreadyThere');
  if (alreadyThere > 0) parts.push(i18n.plural('ALARM.MOVE_ALREADY_THERE', alreadyThere, { app }));
  const remains = count('sourceRemains');
  if (remains > 0) parts.push(i18n.plural('ALARM.MOVE_SOURCE_REMAINS', remains, { app, from: fromApp }));
  const conflicts = count('conflict');
  if (conflicts > 0) parts.push(i18n.plural('ALARM.MOVE_CONFLICT', conflicts, { app }));
  const refusedByReason = new Map<string, number>();
  for (const r of results) {
    if (statusOf(r.status) !== 'refused') continue;
    const reason = reasonText(r, target, i18n);
    refusedByReason.set(reason, (refusedByReason.get(reason) ?? 0) + 1);
  }
  for (const [reason, n] of refusedByReason) parts.push(i18n.plural('ALARM.MOVE_REFUSED', n, { reason }));
  const failed = count('failed');
  if (failed > 0) parts.push(i18n.plural('ALARM.MOVE_FAILED', failed, { app }));

  return parts.join(' ');
}

export function appKey(side: LinkSide): string {
  return side === 'telegram' ? 'DELIVER_TO.TELEGRAM' : 'DELIVER_TO.DISCORD';
}

export function otherSide(side: LinkSide): LinkSide {
  return side === 'telegram' ? 'discord' : 'telegram';
}

/** The two ways an alarm changes apps: one edit that names the other side, or a bulk move from a list. */
@Injectable({ providedIn: 'root' })
export class AlarmMoveService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  /**
   * `PUT /api/{path}/{uid}` with a `destination` naming the other side. The answer is the alarm as it now
   * stands on the target, and `X-Move-Status` says whether the original is gone. A 502 here is worded by the
   * caller (the interceptor stays quiet), and the error is marked so `createFailureText` lets its sentence
   * through.
   */
  edit(path: string, uid: number, body: object, destination: LinkSide, params?: Record<string, string>): Observable<MoveEdit> {
    return this.http
      .put(
        `${this.config.apiHost}/api/${path}/${uid}`,
        { ...body, destination },
        { context: new HttpContext().set(WORDS_OWN_502, true), observe: 'response', ...(params ? { params } : {}) },
      )
      .pipe(
        map(response => ({
          destination,
          status: response.headers.get('X-Move-Status') === 'source_remains' ? ('source_remains' as const) : ('moved' as const),
        })),
        catchError((err: unknown) => throwError(() => markMoveFailure(err))),
      );
  }

  /** Moves a selection. More than 500 uids go out in consecutive requests of at most 500, results joined. */
  moveMany(type: MoveType, uids: readonly number[], destination: LinkSide, kind?: 'event' | 'invasion'): Observable<AlarmMoveResponse> {
    const chunks: number[][] = [];
    for (let i = 0; i < uids.length; i += MAX_MOVE_UIDS) chunks.push(uids.slice(i, i + MAX_MOVE_UIDS));
    return from(chunks).pipe(
      // A chunk that fails must not take the results of the chunks before it down with it: those moves happened.
      concatMap(chunk =>
        this.http
          .post<AlarmMoveResponse>(`${this.config.apiHost}/api/alarm-move`, { uids: chunk, destination, type, ...(kind ? { kind } : {}) })
          .pipe(catchError((err: unknown) => of<AlarmMoveResponse>({ results: failedOutcomes(chunk, err) }))),
      ),
      reduce<AlarmMoveResponse, AlarmMoveResponse>((all, part) => ({ results: [...all.results, ...(part.results ?? [])] }), {
        results: [],
      }),
    );
  }

  /**
   * What every alarm service's `update` goes through. A body naming a `destination` is a move, answered with
   * how it ended; any other body is the service's own plain PUT, which answers nothing, exactly as before.
   * `params` go on the move's query string too, so a move names the same rule the plain PUT would.
   */
  put(
    path: string,
    uid: number,
    body: { destination?: LinkSide },
    plain: () => Observable<void>,
    params?: Record<string, string>,
  ): Observable<MoveEdit | void> {
    return body.destination ? this.edit(path, uid, body, body.destination, params) : plain();
  }
}
