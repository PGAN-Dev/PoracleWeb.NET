import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * What an alarm list shows before it has any rules: one shape for all ten types. A title that says
 * there are none yet, one line on what an alarm of this type would send, and the one thing to do next.
 *
 * Each list used to draw its own: a bespoke illustration, a title in title case ("No Lure Alarms
 * Configured"), a subtitle that repeated the page description, and a button in the type's colour.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  selector: 'app-alarm-empty-state',
  standalone: true,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      max-width: 440px;
      margin: 48px auto;
      padding: 0 16px;
      text-align: center;
    }
    .empty-icon {
      width: 48px;
      height: 48px;
      font-size: 48px;
      color: var(--mat-sys-on-surface-variant, rgba(0, 0, 0, 0.6));
    }
    .empty-title {
      margin: 8px 0 0;
      font-size: 20px;
      font-weight: 600;
      color: var(--mat-sys-on-surface, #1c1b1f);
    }
    .empty-subtitle {
      margin: 0 0 8px;
      font-size: 14px;
      line-height: 1.5;
      color: var(--mat-sys-on-surface-variant, rgba(0, 0, 0, 0.6));
    }
  `,
  template: `
    <div class="empty-state">
      <mat-icon class="empty-icon" aria-hidden="true">{{ icon() }}</mat-icon>
      <h2 class="empty-title">{{ titleKey() | translate }}</h2>
      <p class="empty-subtitle">{{ bodyKey() | translate }}</p>
      <button mat-flat-button type="button" class="empty-action" (click)="add.emit()">
        <mat-icon>add</mat-icon>
        {{ 'ALARM.EMPTY_ADD' | translate }}
      </button>
    </div>
  `,
})
export class AlarmEmptyStateComponent {
  readonly add = output<void>();
  /** One line on what an alarm of this type sends, ending in the reason to add one. */
  readonly bodyKey = input.required<string>();
  /** The type's own icon, the one its add dialog's first tab carries. */
  readonly icon = input.required<string>();
  readonly titleKey = input.required<string>();
}
