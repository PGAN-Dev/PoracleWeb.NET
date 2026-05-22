import { inject, Pipe, PipeTransform } from '@angular/core';

import { resolveLevel } from '../../core/models/raid-level.models';
import { I18nService } from '../../core/services/i18n.service';

/**
 * Resolve a stored raid/egg level integer to its display label.
 *
 * - Levels 1-19              → masterfile names ("1 Star", "Mega Legendary", "Elite", …)
 * - 9000 (wildcard sentinel) → "Any"
 * - Anything else             → "Level {n}" (custom)
 *
 * Graceful degradation: if a translation key is missing for the level (e.g. a
 * future raid_20 ships before the i18n files are updated), ngx-translate
 * returns the literal key string. We detect that case and fall back to the
 * generic "Level {n}" custom format so users see a number rather than
 * "RAIDS.LEVEL.RAID_20".
 */
@Pipe({
  name: 'levelLabel',
  standalone: true,
})
export class LevelLabelPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number): string {
    const opt = resolveLevel(value);
    if (opt.category === 'custom') {
      return this.i18n.instant(opt.labelKey) + ' ' + opt.value;
    }
    const translated = this.i18n.instant(opt.labelKey);
    // ngx-translate returns the key unchanged when the key isn't found —
    // detect that and fall back to a useful generic label rather than leaking
    // the raw translation key into the UI.
    if (translated === opt.labelKey) {
      return this.i18n.instant('RAIDS.LEVEL.CUSTOM') + ' ' + value;
    }
    return translated;
  }
}
