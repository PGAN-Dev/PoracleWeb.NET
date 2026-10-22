import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

import { AddRefusal, ExistingRule } from '../../utils/add-outcome';

/**
 * Why an Add was refused, shown inline above the dialog's actions so the user keeps what they set up.
 * Names the stored rule in the way when the API reported one, with a way to edit that rule instead.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  selector: 'app-add-refusal',
  standalone: true,
  styleUrl: './add-refusal.component.scss',
  templateUrl: './add-refusal.component.html',
})
export class AddRefusalComponent {
  readonly editExisting = output<ExistingRule>();
  readonly refusal = input<AddRefusal | null>(null);
}
