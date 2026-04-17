import { Component, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslateModule } from '@ngx-translate/core';

import { DeliveryPreviewComponent } from '../delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../template-selector/template-selector.component';

export type RaidDeliveryAlarmType = 'raid' | 'egg';
export type RaidDeliveryDistanceMode = 'areas' | 'distance';

@Component({
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatIconModule,
    TranslateModule,
    DeliveryPreviewComponent,
    TemplateSelectorComponent,
  ],
  selector: 'app-raid-delivery-section',
  standalone: true,
  styleUrl: './raid-delivery-section.component.scss',
  templateUrl: './raid-delivery-section.component.html',
})
export class RaidDeliverySectionComponent {
  readonly alarmType = input<RaidDeliveryAlarmType>('raid');
  readonly clean = input.required<FormControl<boolean | null>>();
  readonly distanceKm = input.required<FormControl<number | null>>();
  readonly distanceMode = input.required<FormControl<RaidDeliveryDistanceMode | null>>();
  readonly ping = input.required<FormControl<string | null>>();
  readonly showPing = input(false);
  readonly template = input.required<FormControl<string | null>>();

  onDistanceModeChange(): void {
    const mode = this.distanceMode();
    const km = this.distanceKm();
    if (mode.value === 'areas') {
      km.setValue(0);
    } else if (!km.value) {
      km.setValue(1);
    }
  }
}
