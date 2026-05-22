import { Component, computed, EventEmitter, inject, Input, Output, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import {
  ANY_LEVEL,
  ANY_LEVEL_VALUE,
  isBuiltInLevel,
  LevelOption,
  makeCustomLevel,
  SPECIAL_LEVELS,
  STANDARD_LEVELS,
} from '../../../core/models/raid-level.models';
import { CustomLevelStore } from '../../../core/services/custom-level-store.service';

/**
 * Three-section chip-based selector for raid/egg levels. Backed by
 * {@link CustomLevelStore} so user-added custom values persist across sessions.
 *
 * Use `multiple=true` for the raid/egg multi-select case. Use `multiple=false`
 * for the boss-level single-select. Set `showAny=true` to surface the 9000
 * "Any" sentinel as a first-class chip (typical for single-select).
 */
@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatTooltipModule,
    TranslateModule,
  ],
  selector: 'app-level-selector',
  standalone: true,
  styleUrl: './level-selector.component.scss',
  templateUrl: './level-selector.component.html',
})
export class LevelSelectorComponent {
  private readonly customLevels = inject(CustomLevelStore);
  private readonly translate = inject(TranslateService);

  /** Inline validation error key, if any. */
  protected readonly addInputError = signal<string | null>(null);
  /** Whether the "+ Add level" input is currently expanded. */
  protected readonly addInputOpen = signal(false);
  /** Current text in the add-custom input. */
  protected readonly addInputValue = signal('');

  protected readonly anyLevel = ANY_LEVEL;

  @ViewChild('customInput') customInput?: { nativeElement: HTMLInputElement };
  /** Value that should flash briefly after a duplicate add attempt. */
  protected readonly flashValue = signal<number | null>(null);
  @Input() multiple = true;

  /** Custom palette (from the store). */
  protected readonly palette = computed<LevelOption[]>(() => this.customLevels.values().map(makeCustomLevel));
  /** Current selection. Internal signal; pushed in via `value` setter, out via `valueChange`. */
  protected readonly selected = signal<number[]>([]);

  /** When true, surface the 9000 "Any" sentinel as a dedicated chip in SPECIAL. */
  @Input() showAny = false;
  protected readonly specialLevels = SPECIAL_LEVELS;
  protected readonly standardLevels = STANDARD_LEVELS;
  @Output() readonly valueChange = new EventEmitter<number[]>();

  @Input()
  set value(next: number[] | null | undefined) {
    const safe = (next ?? []).filter(v => Number.isInteger(v) && v >= 1);
    this.selected.set(safe);
    // Surface any custom values from incoming alarms into the palette so they
    // appear pre-selected in the CUSTOM row rather than being orphaned.
    const customs = safe.filter(v => !isBuiltInLevel(v));
    if (customs.length > 0) this.customLevels.seedFrom(customs);
  }

  protected cancelAddInput(): void {
    this.addInputOpen.set(false);
    this.addInputValue.set('');
    this.addInputError.set(null);
  }

  protected commitAddInput(): void {
    const raw = this.addInputValue().trim();
    if (raw === '') {
      this.cancelAddInput();
      return;
    }
    const parsed = Number.parseInt(raw, 10);
    if (!Number.isInteger(parsed) || parsed < 1 || String(parsed) !== raw.replace(/^0+(\d)/, '$1')) {
      this.addInputError.set('RAIDS.LEVEL.INVALID');
      return;
    }

    // The "Any" sentinel snaps to its dedicated chip — don't create a duplicate
    // custom entry. If showAny is off, still treat it as a special case for clarity.
    if (parsed === ANY_LEVEL_VALUE) {
      if (this.showAny) {
        this.cancelAddInput();
        if (!this.isSelected(ANY_LEVEL_VALUE)) this.toggle(ANY_LEVEL_VALUE);
        this.flash(ANY_LEVEL_VALUE);
        return;
      }
      // showAny is off but user wants Any — accept it as a custom value rather than blocking.
    }

    // Duplicate of an existing built-in chip — flash it instead of erroring.
    if (isBuiltInLevel(parsed)) {
      this.cancelAddInput();
      if (!this.isSelected(parsed)) this.toggle(parsed);
      this.flash(parsed);
      return;
    }

    // Duplicate of an existing custom chip — flash + select if not already.
    if (this.palette().some(o => o.value === parsed)) {
      this.addInputError.set(this.translate.instant('RAIDS.LEVEL.DUPLICATE', { value: parsed }));
      this.flash(parsed);
      if (!this.isSelected(parsed)) this.toggle(parsed);
      return;
    }

    this.customLevels.add(parsed);
    this.cancelAddInput();
    if (!this.isSelected(parsed)) this.toggle(parsed);
  }

  protected isSelected(value: number): boolean {
    return this.selected().includes(value);
  }

  protected onAddKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.commitAddInput();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.cancelAddInput();
    }
  }

  protected openAddInput(): void {
    this.addInputOpen.set(true);
    this.addInputValue.set('');
    this.addInputError.set(null);
    queueMicrotask(() => this.customInput?.nativeElement.focus());
  }

  protected removeCustom(value: number, event: MouseEvent): void {
    event.stopPropagation();
    this.customLevels.remove(value);
    if (this.selected().includes(value)) {
      this.toggle(value);
    }
  }

  protected toggle(value: number): void {
    const current = this.selected();
    let next: number[];
    if (this.multiple) {
      next = current.includes(value) ? current.filter(v => v !== value) : [...current, value];
    } else {
      // Single-select: clicking the active chip clears; otherwise replace.
      next = current.includes(value) && current.length === 1 ? [] : [value];
    }
    this.selected.set(next);
    this.valueChange.emit(next);
  }

  private flash(value: number): void {
    this.flashValue.set(value);
    setTimeout(() => {
      if (this.flashValue() === value) this.flashValue.set(null);
    }, 600);
  }
}
