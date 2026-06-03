import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButtonToggleModule, TranslateModule],
  selector: 'app-rsvp-toggle',
  standalone: true,
  styleUrl: './rsvp-toggle.component.scss',
  templateUrl: './rsvp-toggle.component.html',
})
export class RsvpToggleComponent {
  readonly control = input.required<FormControl<number | null>>();

  /** i18n key describing the currently selected mode, shown as a hint below the toggle. */
  descriptionKey(): string {
    switch (this.control().value) {
      case 1:
        return 'RAIDS.RSVP_INCLUDE_DESC';
      case 2:
        return 'RAIDS.RSVP_ONLY_DESC';
      default:
        return 'RAIDS.RSVP_OFF_DESC';
    }
  }
}
