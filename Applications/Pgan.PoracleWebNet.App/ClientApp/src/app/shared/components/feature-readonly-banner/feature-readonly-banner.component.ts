import { Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

import { SettingsService } from '../../../core/services/settings.service';

/**
 * Explains why an alarm page has lost its create and edit controls.
 *
 * A disabled alarm type is no longer hidden outright: the rules a user already has stay listed and
 * removable, because deleting is the one action still worth taking — and when the type is disabled in
 * Poracle rather than here, its bot refuses the matching command too, leaving this page as the only
 * place to clean up. What the page cannot do is make more of something that can never fire.
 *
 * The wording differs by source. An admin can undo their own setting; nobody can undo Poracle's from
 * this side, so saying "an administrator has disabled this" there would send them to the wrong person.
 */
@Component({
  imports: [MatIconModule, TranslatePipe],
  selector: 'app-feature-readonly-banner',
  standalone: true,
  styles: [
    `
      .readonly-banner {
        display: flex;
        align-items: center;
        gap: 10px;
        margin: 0 0 16px;
        padding: 12px 16px;
        border: 1px solid rgba(255, 152, 0, 0.4);
        border-radius: 8px;
        background: rgba(255, 152, 0, 0.08);
        color: var(--text-primary, rgba(0, 0, 0, 0.87));
        font-size: 14px;
        line-height: 1.5;
      }

      .readonly-banner mat-icon {
        flex: 0 0 auto;
        color: #ef6c00;
      }
    `,
  ],
  template: `
    @if (disabled()) {
      <div class="readonly-banner" role="status">
        <mat-icon>lock</mat-icon>
        <span>{{ (forcedByPoracle() ? 'ALARM.READ_ONLY_PORACLE' : 'ALARM.READ_ONLY_ADMIN') | translate }}</span>
      </div>
    }
  `,
})
export class FeatureReadonlyBannerComponent {
  private readonly settings = inject(SettingsService);

  /** The `disable_*` key for this alarm type, e.g. `disable_lures`. */
  readonly disableKey = input.required<string>();

  readonly disabled = computed(() => this.settings.isDisabled(this.disableKey()));

  readonly forcedByPoracle = computed(() => this.settings.isForcedByPoracle(this.disableKey()));
}
