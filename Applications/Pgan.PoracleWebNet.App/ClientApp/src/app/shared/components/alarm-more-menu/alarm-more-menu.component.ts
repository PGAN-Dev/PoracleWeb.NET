import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { TestAlertService } from '../../../core/services/test-alert.service';

/** The alarm types PoracleWeb.NET can send a sample for. A raid list sends `raid` or `egg` by the card, never by the list. */
export type TestAlertType = 'egg' | 'gym' | 'invasion' | 'lure' | 'nest' | 'pokemon' | 'quest' | 'raid';

/**
 * The "More actions" button at the end of an alarm card's action row, and the menu it opens.
 *
 * Sending a test notification used to be a paper-plane button beside Edit, and members pressed it by accident
 * and went looking for a spawn that was never there (#1097). Here a stray press only opens a menu; sending is a
 * second, deliberate choice, and the item says in its own words that the result is a sample.
 *
 * `TestAlertService` still owns the request: the 15 second cooldown, the in-flight dedupe and every snackbar.
 * This component only reads its two states (sending, cooling down) and asks it to send. While a send is in
 * flight the button itself turns into a spinner, because the menu has already closed by then and the card
 * would otherwise show nothing.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe],
  selector: 'app-alarm-more-menu',
  standalone: true,
  styleUrl: './alarm-more-menu.component.scss',
  templateUrl: './alarm-more-menu.component.html',
})
export class AlarmMoreMenuComponent {
  private readonly testAlert = inject(TestAlertService);

  /** Which alarm type to send a sample for. A raid list passes `raid` on a raid card and `egg` on an egg card. */
  readonly alarmType = input.required<TestAlertType>();

  /** The alarm's uid. */
  readonly uid = input.required<number>();

  /** True for the 15 seconds after a send, or after an unsupported type answered 501. */
  readonly coolingDown = computed(() => this.testAlert.isCoolingDown(this.alarmType(), this.uid()));

  /** True while the request is in flight. */
  readonly sending = computed(() => this.testAlert.isSending(this.alarmType(), this.uid()));

  sendTest(): void {
    this.testAlert.sendTestAlert(this.alarmType(), this.uid());
  }
}
