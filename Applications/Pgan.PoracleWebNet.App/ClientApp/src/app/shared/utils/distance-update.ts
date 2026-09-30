/** Anything that translates a key: the lists hold I18nService, which wraps TranslateService. */
export interface Translator {
  instant(key: string, params?: Record<string, unknown>): string;
}

/**
 * What `PUT /api/{type}/distance` and `/distance/bulk` report.
 *
 * `updated` counts the alarms that took the new radius. The two skip lists name the selected alarms the
 * server left alone because a radius cannot apply to their scope: one limited to areas cannot take a
 * radius above zero, and one measured from a saved place cannot take zero. PoracleNG refuses both, so
 * sending them failed the whole selection; the server now skips them and says so.
 */
export interface DistanceUpdateResult {
  skippedAreaScoped?: number[];
  skippedPlaceScoped?: number[];
  updated: number;
}

/** Adds together the results of one action that spans two endpoints (raids and eggs share a list). */
export function combineDistanceResults(...results: (DistanceUpdateResult | null | undefined)[]): DistanceUpdateResult {
  return results.reduce<Required<DistanceUpdateResult>>(
    (total, result) => ({
      skippedAreaScoped: [...total.skippedAreaScoped, ...(result?.skippedAreaScoped ?? [])],
      skippedPlaceScoped: [...total.skippedPlaceScoped, ...(result?.skippedPlaceScoped ?? [])],
      updated: total.updated + (result?.updated ?? 0),
    }),
    { skippedAreaScoped: [], skippedPlaceScoped: [], updated: 0 },
  );
}

/**
 * The snack line for a radius change: the page's own success line, then one clause for each kind of
 * alarm that kept its scope. Without the clause the page reports every selected alarm as changed.
 */
export function distanceUpdateMessage(result: DistanceUpdateResult | null | undefined, success: string, translate: Translator): string {
  const parts = [success];
  const areas = result?.skippedAreaScoped?.length ?? 0;
  const place = result?.skippedPlaceScoped?.length ?? 0;

  if (areas > 0) parts.push(translate.instant('WHERE.DISTANCE_SKIPPED_AREAS', { count: areas }));
  if (place > 0) parts.push(translate.instant('WHERE.DISTANCE_SKIPPED_PLACE', { count: place }));

  return parts.join(' ');
}

/** True when the server left some of the selection alone, so the snack stays up long enough to read. */
export function skippedAny(result: DistanceUpdateResult | null | undefined): boolean {
  return (result?.skippedAreaScoped?.length ?? 0) + (result?.skippedPlaceScoped?.length ?? 0) > 0;
}
