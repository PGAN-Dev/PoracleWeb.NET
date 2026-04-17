import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule } from '@ngx-translate/core';

import { GymPickerComponent } from '../gym-picker/gym-picker.component';
import { RsvpToggleComponent } from '../rsvp-toggle/rsvp-toggle.component';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatFormFieldModule, MatSelectModule, TranslateModule, RsvpToggleComponent, GymPickerComponent],
  selector: 'app-raid-settings-section',
  standalone: true,
  styleUrl: './raid-settings-section.component.scss',
  templateUrl: './raid-settings-section.component.html',
})
export class RaidSettingsSectionComponent {
  readonly gymId = model<string | null>(null);
  readonly rsvpControl = input.required<FormControl<number | null>>();
  readonly teamControl = input.required<FormControl<number | null>>();
}
