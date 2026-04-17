import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButtonToggleModule, MatIconModule, TranslateModule],
  selector: 'app-rsvp-toggle',
  standalone: true,
  styleUrl: './rsvp-toggle.component.scss',
  templateUrl: './rsvp-toggle.component.html',
})
export class RsvpToggleComponent {
  readonly control = input.required<FormControl<number | null>>();
}
