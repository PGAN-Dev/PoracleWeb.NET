import { Component, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';

import { DistanceDisplayPipe } from '../../pipes/distance-display.pipe';

@Component({
  imports: [MatChipsModule, MatIconModule, MatTooltipModule, DistanceDisplayPipe, TranslateModule],
  selector: 'app-alarm-info',
  standalone: true,
  styleUrl: './alarm-info.component.scss',
  templateUrl: './alarm-info.component.html',
})
export class AlarmInfoComponent {
  clean = input(0);
  distance = input(0);
  ping = input<string | null>(null);
  template = input<string | null>(null);
}
