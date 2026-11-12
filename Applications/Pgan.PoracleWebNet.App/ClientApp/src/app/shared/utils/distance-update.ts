/** Anything that translates a key: the lists hold I18nService, which wraps TranslateService. */
export interface Translator {
  instant(key: string, params?: Record<string, unknown>): string;
  /** A counted message, by the display language's plural rules (I18nService.plural). */
  plural(base: string, count: number, params?: Record<string, unknown>): string;
}

/**
 * What `PUT /api/{type}/distance` and `/distance/bulk` report.
 *
 * `updated` counts the alarms that took the new radius. The two skip lists name the selected alarms the
 * server left alone because a radius cannot apply to their scope: one limited to areas cannot take a
 * radius above zero, and one measured from a saved place cannot take zero. PoracleNG refuses both, so
 * sending them failed the whole selection; the server now skips them and says so. `skippedConflict` names
 * the rules PoracleNG refused because the new radius would make them clash with another rule of the same
 * user (identical to it, or one updatable field from taking it over), along with any refusal the server
 * could not otherwise explain; the rest of the selection is still updated.
 */
export interface DistanceUpdateResult {
  skippedAreaScoped?: number[];
  skippedConflict?: number[];
  skippedPlaceScoped?: number[];
  updated: number;
}

/** Adds together the results of one action that spans two endpoints (raids and eggs share a list). */
export function combineDistanceResults(...results: (DistanceUpdateResult | null | undefined)[]): DistanceUpdateResult {
  return results.reduce<Required<DistanceUpdateResult>>(
    (total, result) => ({
      skippedAreaScoped: [...total.skippedAreaScoped, ...(result?.skippedAreaScoped ?? [])],
      skippedConflict: [...total.skippedConflict, ...(result?.skippedConflict ?? [])],
      skippedPlaceScoped: [...total.skippedPlaceScoped, ...(result?.skippedPlaceScoped ?? [])],
      updated: total.updated + (result?.updated ?? 0),
    }),
    { skippedAreaScoped: [], skippedConflict: [], skippedPlaceScoped: [], updated: 0 },
  );
}

/**
 * The snack line for a radius change: the page's own success line, then one clause for each kind of
 * alarm that kept its scope. Without the clause the page reports every selected alarm as changed.
 */
export function distanceUpdateMessage(
  result: DistanceUpdateResult | null | undefined,
  success: string,
  translate: Translator,
  conflictNames: readonly string[] = [],
): string {
  const clauses: string[] = [];
  const areas = result?.skippedAreaScoped?.length ?? 0;
  const place = result?.skippedPlaceScoped?.length ?? 0;
  const conflict = result?.skippedConflict?.length ?? 0;

  if (areas > 0) clauses.push(translate.plural('WHERE.DISTANCE_SKIPPED_AREAS', areas));
  if (place > 0) clauses.push(translate.plural('WHERE.DISTANCE_SKIPPED_PLACE', place));
  if (conflict > 0) {
    clauses.push(translate.plural('WHERE.DISTANCE_SKIPPED_CONFLICT', conflict));
    // "Skipped 1 that would clash" leaves the user to hunt for which one. The caller knows its own list.
    if (conflictNames.length > 0) clauses.push(translate.instant('WHERE.DISTANCE_SKIPPED_NAMES', { names: joinNames(conflictNames) }));
  }
  if (clauses.length === 0) return success;

  // The success lines are written to stand alone and carry no full stop; two sentences run together
  // without one.
  const lead = /[.!?]$/.test(success) ? success : `${success}.`;
  return [lead, ...clauses].join(' ');
}

/** True when the server left some of the selection alone, so the snack stays up long enough to read. */
export function skippedAny(result: DistanceUpdateResult | null | undefined): boolean {
  return (result?.skippedAreaScoped?.length ?? 0) + (result?.skippedPlaceScoped?.length ?? 0) + (result?.skippedConflict?.length ?? 0) > 0;
}

/** Up to three names, then how many more: a long selection must not push the toast off the screen. */
function joinNames(names: readonly string[]): string {
  const shown = names.slice(0, 3).join(', ');
  return names.length > 3 ? `${shown} +${names.length - 3}` : shown;
}

/** The titles of the listed rules the server named, once each. A uid the list no longer holds is left out. */
export function skippedNames<T extends { uid: number }>(
  uids: readonly number[] | null | undefined,
  items: readonly T[],
  label: (item: T) => string,
): string[] {
  const names = new Set<string>();
  for (const uid of uids ?? []) {
    const item = items.find(i => i.uid === uid);
    if (item) names.add(label(item));
  }
  return [...names];
}
