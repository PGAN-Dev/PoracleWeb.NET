import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, MatTooltipModule, TranslateModule],
  selector: 'app-location-warning',
  standalone: true,
  styleUrl: './location-warning.component.scss',
  templateUrl: './location-warning.component.html',
})
export class LocationWarningComponent {
  readonly hasActiveHours = input(false);
  readonly latitude = input(0);
  readonly longitude = input(0);

  readonly show = computed(() => this.hasActiveHours() && this.latitude() === 0 && this.longitude() === 0);
}
