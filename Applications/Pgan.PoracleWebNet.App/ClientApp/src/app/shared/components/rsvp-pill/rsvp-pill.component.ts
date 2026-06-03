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

  readonly labelKey = computed(() => {
    switch (this.value()) {
      case 1:
        return 'RAIDS.RSVP_PILL_INCLUDE';
      case 2:
        return 'RAIDS.RSVP_PILL_ONLY';
      default:
        return null;
    }
  });
}
