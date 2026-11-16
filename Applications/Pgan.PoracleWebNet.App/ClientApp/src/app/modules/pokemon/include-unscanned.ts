import { AbstractControl } from '@angular/forms';

/** Minior's Pokedex number. Its dialogs explain which core colors Golbat derives. */
export const MINIOR_ID = 774;

/**
 * Keeps the "alert at first sight" switch and the Min IV box consistent.
 *
 * Golbat derives IVs from the encounter id, so most spawns carry IVs the moment they appear. Poracle
 * treats min_iv -1 as "match everything, derived IVs included, at first sight"; min_iv 0 matches only
 * spawns whose IVs were observed in an encounter; above 0 also matches derived IVs. The saved values are
 * unchanged, only their meaning moved. While the switch is on the Min IV box is disabled rather than
 * silently ignored. (The control keeps its old name, includeUnscanned, to avoid churning saved state.)
 */
export function syncUnscanned(controls: {
  includeUnscanned: AbstractControl<boolean | null>;
  minIv: AbstractControl<number | null>;
}): void {
  const apply = (on: boolean | null): void => {
    if (on) {
      controls.minIv.disable({ emitEvent: false });
    } else {
      controls.minIv.enable({ emitEvent: false });
    }
  };
  apply(controls.includeUnscanned.value);
  controls.includeUnscanned.valueChanges.subscribe(apply);
}
