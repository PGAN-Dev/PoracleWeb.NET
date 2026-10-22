import { WritableSignal } from '@angular/core';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { catchError, Observable, of } from 'rxjs';

import { describeScope, scopeOf } from './alarm-scope';
import { serverReason } from './server-reason';
import { reportedByInterceptor } from '../../core/interceptors/error.interceptor';
import { I18nService } from '../../core/services/i18n.service';

/** A create the API refused, kept beside the ones that landed so one refusal cannot hide the rest (#577). */
export interface FailedCreate {
  failed: unknown;
}

/** One create, settled: the alarm the API answered with (uid 0 when it was already tracked), or the refusal. */
export type SettledCreate = FailedCreate | { uid?: number };

/** A refused single Add, as the dialog shows it above its actions. */
export interface AddRefusal {
  /** The stored rule in the way, when the API named it and the list that opened the dialog holds it. */
  existing: ExistingRule | null;
  message: string;
}

/** A stored rule an add dialog can point at. */
export interface ExistingRule {
  summary: string;
  /** PoracleNG's type name (`pokemon`, `raid`, `egg`, `incident`, ...), which tells raid and egg uids apart. */
  trackingType: null | string;
  uid: number;
}

/**
 * Describes a stored rule of the list that opened the dialog in a few words, or answers null when the list
 * holds no rule by that uid and type, so the dialog never offers to open a rule the list can't find.
 */
export type ExistingRuleLookup = (uid: number, trackingType: null | string) => null | string;

/** What an alarm list passes to its add dialog. */
export interface AddDialogData {
  existingRule?: ExistingRuleLookup;
}

/** The scope fields every alarm carries. */
export interface ScopedRule {
  distance?: null | number;
  overrideAreas?: null | string[];
  overrideLocationLabel?: null | string;
}

/** "Snorlax · Within 2 km of my pin": what the rule is about, then where it reaches you, as the card says it. */
export function ruleSummary(
  title: string,
  rule: ScopedRule,
  profileAreas: string[],
  translate: TranslateService,
  areaName?: (area: string) => string,
): string {
  const where = describeScope(
    scopeOf(rule.overrideLocationLabel, rule.overrideAreas, rule.distance ?? 0),
    profileAreas,
    translate,
    areaName,
  );
  return title ? `${title} · ${where}` : where;
}

/** The add dialog closed asking its list to open this rule's edit dialog. */
export interface EditExistingResult {
  editExisting: { trackingType: null | string; uid: number };
}

export function isEditExisting(result: unknown): result is EditExistingResult {
  const target = (result as EditExistingResult | null | undefined)?.editExisting;
  return !!target && typeof target.uid === 'number' && target.uid > 0;
}

/** The close result that asks the list to open `rule` for editing. */
export function editExistingResult(rule: ExistingRule): EditExistingResult {
  return { editExisting: { uid: rule.uid, trackingType: rule.trackingType } };
}

/** Lets a create settle on its own instead of failing the whole forkJoin. */
export function settle<T extends { uid?: number }>(request: Observable<T>): Observable<FailedCreate | T> {
  return request.pipe(catchError((failed: unknown) => of({ failed })));
}

export function isFailed(result: SettledCreate): result is FailedCreate {
  return 'failed' in result;
}

/**
 * The first message of an ASP.NET validation 400 (`{ errors: { Field: ["..."] } }`), or null. Model binding
 * answers in that shape when a field is out of range, and it names the field, which a fixed line can't.
 */
export function validationReason(err: unknown): string | null {
  const failure = err as { error?: { errors?: unknown }; status?: unknown } | null | undefined;
  if (typeof failure?.status === 'number' && failure.status >= 500) return null;
  const errors = failure?.error?.errors;
  if (!errors || typeof errors !== 'object') return null;
  for (const messages of Object.values(errors as Record<string, unknown>)) {
    const first = Array.isArray(messages) ? messages.find(m => typeof m === 'string' && m.trim()) : null;
    if (typeof first === 'string') return first;
  }
  return null;
}

/**
 * What a create or edit failure says when the server gave no reason of its own. It only reaches the screen for
 * statuses the interceptor doesn't toast (a 400 or 409 without text, a 422, 429, 501), so the next step
 * is about the settings, or for a 429 about waiting, never the connection.
 */
export function createFailureText(err: unknown, i18n: I18nService, fallbackKey: string): string {
  const status = (err as { status?: unknown } | null | undefined)?.status;
  return serverReason(err) ?? validationReason(err) ?? i18n.instant(status === 429 ? 'ALARM.FAILED_TOO_MANY' : fallbackKey);
}

/**
 * Turns a refused create into what the dialog shows: the server's reason (or `fallback`), and the stored
 * rule it collided with when the 409 named one (`existingUid`) and `lookup` can describe it.
 *
 * Null when the error interceptor has already toasted the failure (a 5xx, a dropped connection, a 403 or
 * 404): the dialog still stays open with the user's settings, and the toast is the one message they read.
 */
export function refusalOf(err: unknown, fallback: string, lookup?: ExistingRuleLookup | null): AddRefusal | null {
  if (reportedByInterceptor(err)) return null;

  const body = (err as { error?: { existingUid?: unknown; trackingType?: unknown } } | null | undefined)?.error;
  const uid = typeof body?.existingUid === 'number' && body.existingUid > 0 ? body.existingUid : null;
  const trackingType = typeof body?.trackingType === 'string' ? body.trackingType : null;
  const summary = uid !== null && lookup ? lookup(uid, trackingType) : null;

  return {
    existing: uid !== null && summary ? { uid, summary, trackingType } : null,
    message: serverReason(err) ?? validationReason(err) ?? fallback,
  };
}

export interface FinishAddOptions {
  dialogRef: MatDialogRef<unknown>;
  /** Translation key for a refusal the server gave no reason for. */
  failedKey: string;
  i18n: I18nService;
  lookup?: ExistingRuleLookup | null;
  /** Set when a single Add is refused; the dialog renders it and stays open. */
  refusal: WritableSignal<AddRefusal | null>;
  snackBar: MatSnackBar;
}

/**
 * Reports an add dialog's settled creates, and closes it unless a single Add was refused.
 *
 * A single Add the API refuses created nothing, so the dialog stays open with the reason above its actions
 * and the user's settings intact. A batch (several Pokémon, levels, lures...) closes as it always has:
 * whatever landed is real and the list must reload to show it, and the first refusal is toasted.
 */
export function finishAdd(results: SettledCreate[], options: FinishAddOptions): void {
  const { dialogRef, failedKey, i18n, lookup, refusal, snackBar } = options;
  const refused = results.filter(isFailed);

  if (results.length === 1 && refused.length === 1) {
    refusal.set(refusalOf(refused[0].failed, createFailureText(refused[0].failed, i18n, failedKey), lookup));
    return;
  }

  // Three outcomes, not two: refused, already tracked (200 with uid 0, see #495) and created.
  const landed = results.filter((r): r is { uid?: number } => !isFailed(r));
  const created = landed.filter(r => (r.uid ?? 0) > 0).length;
  const duplicates = landed.length - created;

  if (refused.length > 0) {
    // A row the interceptor already toasted (an outage, a dropped connection) keeps that one message; the
    // settings-oriented fallback would send the user to their filters for a server failure.
    const unreported = refused.find(r => !reportedByInterceptor(r.failed));
    if (unreported) {
      snackBar.open(createFailureText(unreported.failed, i18n, failedKey), i18n.instant('COMMON.OK'), { duration: 6000 });
    }
  } else {
    reportCreated(created, duplicates, i18n, snackBar);
  }

  dialogRef.close(true);
}

/**
 * The one snack every add dialog shows once its creates have landed: "3 alarms created", "1 created,
 * 2 already tracked", or, for a single Add PoracleNG found stored exactly, that nothing was changed.
 * Lures, fort changes and Pokéstop events each used to word it their own way.
 */
export function reportCreated(created: number, duplicates: number, i18n: I18nService, snackBar: MatSnackBar): void {
  if (created === 0 && duplicates === 1) {
    // PoracleNG left the stored rule exactly as it was. "0 created, 1 already tracked" read as success
    // while a setting the user changed (Clean mode, say) was dropped.
    snackBar.open(i18n.instant('ALARM.ALREADY_TRACKED_ONE'), i18n.instant('COMMON.OK'), { duration: 6000 });
    return;
  }
  const message =
    duplicates > 0
      ? i18n.instant('ALARM.SNACK_CREATED_WITH_DUPLICATES', { count: created, duplicates })
      : i18n.plural('ALARM.SNACK_CREATED', created);
  snackBar.open(message, i18n.instant('COMMON.OK'), { duration: 4000 });
}
