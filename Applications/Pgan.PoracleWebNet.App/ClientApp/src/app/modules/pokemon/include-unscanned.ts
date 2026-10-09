import { AbstractControl } from '@angular/forms';

/**
 * Keeps the "include Pokemon nobody has scanned yet" switch and the Min IV box consistent.
 *
 * A Pokemon nobody has encountered has no IV, which Poracle treats as -1 and drops when it is below the
 * rule's min_iv. Saving min_iv -1 is how a rule asks for those spawns, and it also lets every
 * encountered IV through, so while the switch is on the Min IV box is disabled rather than silently
 * ignored.
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
