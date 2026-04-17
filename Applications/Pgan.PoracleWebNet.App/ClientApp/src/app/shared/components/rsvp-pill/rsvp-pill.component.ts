import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslateModule],
  selector: 'app-rsvp-pill',
  standalone: true,
  styleUrl: './rsvp-pill.component.scss',
  templateUrl: './rsvp-pill.component.html',
})
export class RsvpPillComponent {
  readonly value = input<number | null | undefined>(0);

  readonly labelKey = computed(() => (this.value() === 1 ? 'RAIDS.RSVP_PILL_INCLUDE' : 'RAIDS.RSVP_PILL_ONLY'));
  readonly show = computed(() => (this.value() ?? 0) > 0);
}
