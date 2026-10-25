import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatRadioModule } from '@angular/material/radio';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { startWith, switchMap } from 'rxjs';

import { LinkSide } from '../../../core/models';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { AppGlyphComponent } from '../app-glyph/app-glyph.component';

let nextId = 0;

/**
 * "Which app gets this alarm? Discord / Telegram", asked in every alarm dialog, above its tabs so the chosen
 * app is in view whichever tab is open (it used to wait on the Delivery tab, beside "Where should this alert
 * reach you?", while an add quietly defaulted it).
 *
 * It renders only for a person whose two accounts are linked and both exist, so everyone else sees the
 * dialogs exactly as before. A paused side stays selectable (the alarm is saved and waits), with one line
 * saying nothing will arrive yet and linking to Linked accounts, which says why: PoracleWeb.NET never
 * switches a side back on for the person.
 *
 * The control starts empty in an add dialog and the field seeds it from the person's default app; an edit
 * dialog seeds it from the alarm's own destination before this renders, so that value is left alone.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppGlyphComponent, MatRadioModule, ReactiveFormsModule, RouterLink, TranslatePipe],
  selector: 'app-deliver-to-field',
  standalone: true,
  styleUrl: './deliver-to-field.component.scss',
  templateUrl: './deliver-to-field.component.html',
})
export class DeliverToFieldComponent {
  private readonly linked = inject(LinkedAccountService);

  readonly control = input.required<FormControl<LinkSide | null>>();

  private readonly value = toSignal(
    toObservable(this.control).pipe(switchMap(control => control.valueChanges.pipe(startWith(control.value)))),
    { initialValue: null as LinkSide | null },
  );

  readonly labelId = `deliver-to-${nextId++}`;

  /** The app the chosen side is, when that side is not delivering; otherwise null. */
  readonly pausedApp = computed<LinkSide | null>(() => {
    const chosen = this.value();
    const side = this.linked.status()?.sides?.find(s => s.side === chosen);
    return chosen && side && side.pause !== 'none' ? chosen : null;
  });

  readonly visible = this.linked.canChoose;

  constructor() {
    effect(() => {
      if (!this.visible()) return;
      const control = this.control();
      untracked(() => {
        if (control.value) return;
        control.setValue(this.linked.status()?.defaultDestination ?? 'discord');
      });
    });
  }
}
