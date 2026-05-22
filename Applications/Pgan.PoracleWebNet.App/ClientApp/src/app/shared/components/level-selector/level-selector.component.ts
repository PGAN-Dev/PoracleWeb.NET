import { Component, computed, ElementRef, EventEmitter, inject, Input, Output, signal, ViewChild } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
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
 * Compact single-row chip selector for raid/egg/boss levels. Standard, special,
 * and custom chips all live in one wrapping flow; categories are encoded in
 * chip content (T1 vs "Mega · 6" vs "42 ⊗") rather than in container labels.
 *
 * Required input: `paletteKey` — scopes the custom-level palette so additions
 * on the raid picker don't bleed into the egg or boss pickers.
 *
 * Use `multiple=true` for the raid/egg multi-select case. Use `multiple=false`
 * for the boss-level single-select. Set `showAny=true` to surface the 9000
 * "Any" sentinel as a first-class chip — only do this where PoracleNG actually
 * honors 9000 as a wildcard (raids and bosses, NOT eggs).
 */
@Component({
  imports: [MatChipsModule, MatIconModule, MatSnackBarModule, MatTooltipModule, TranslateModule],
  selector: 'app-level-selector',
  standalone: true,
  styleUrl: './level-selector.component.scss',
  templateUrl: './level-selector.component.html',
})
export class LevelSelectorComponent {
  /** Explicit two-state machine for the add affordance — avoids `!` negation bugs in templates. */
  private readonly addMode = signal<'closed' | 'open'>('closed');
  private readonly customLevels = inject(CustomLevelStore);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  @ViewChild('addInput') addInput?: ElementRef<HTMLInputElement>;
  /** Inline validation error key, if any. */
  protected readonly addInputError = signal<string | null>(null);
  /** Current text in the add-custom input. */
  protected readonly addInputValue = signal('');

  protected readonly anyLevel = ANY_LEVEL;
  /** Value that should flash briefly after a duplicate add attempt. */
  protected readonly flashValue = signal<number | null>(null);
  protected isAddClosed = () => this.addMode() === 'closed';
  protected isAddOpen = () => this.addMode() === 'open';

  @Input() multiple = true;
  /** Identifier for the custom-level palette (`raid` / `egg` / `boss`). Required. */
  @Input({ required: true }) paletteKey!: string;
  /** Custom palette (from the store, scoped by paletteKey). */
  protected readonly palette = computed<LevelOption[]>(() => this.customLevels.values(this.paletteKey).map(makeCustomLevel));

  /** Current selection. Internal signal; pushed in via `value` setter, out via `valueChange`. */
  protected readonly selected = signal<number[]>([]);
  /** When true, surface the 9000 "Any" sentinel as a dedicated chip. */
  @Input() showAny = false;
  protected readonly specialLevels = SPECIAL_LEVELS;
  protected readonly standardLevels = STANDARD_LEVELS;
  @Output() readonly valueChange = new EventEmitter<number[]>();

  @Input()
  set value(next: number[] | null | undefined) {
    const safe = (next ?? []).filter(v => Number.isInteger(v) && v >= 1);
    this.selected.set(safe);
    // Surface any custom values from incoming alarms into THIS palette so they
    // appear pre-selected rather than orphaned. Scoped by paletteKey.
    const customs = safe.filter(v => !isBuiltInLevel(v));
    if (customs.length > 0 && this.paletteKey) this.customLevels.seedFrom(this.paletteKey, customs);
  }

  protected cancelAddInput(): void {
    this.addMode.set('closed');
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

    // The "Any" sentinel snaps to its dedicated chip when surfaced.
    if (parsed === ANY_LEVEL_VALUE && this.showAny) {
      this.closeAdd();
      if (!this.isSelected(ANY_LEVEL_VALUE)) this.toggle(ANY_LEVEL_VALUE);
      this.flash(ANY_LEVEL_VALUE);
      return;
    }

    // Duplicate of an existing built-in chip — flash it instead of erroring.
    if (isBuiltInLevel(parsed)) {
      this.closeAdd();
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

    this.customLevels.add(this.paletteKey, parsed);
    this.closeAdd();
    if (!this.isSelected(parsed)) this.toggle(parsed);
  }

  protected isSelected(value: number): boolean {
    return this.selected().includes(value);
  }

  protected onAddInput(event: Event): void {
    const v = (event.target as HTMLInputElement).value;
    this.addInputValue.set(v);
    if (this.addInputError()) this.addInputError.set(null);
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
    this.addMode.set('open');
    this.addInputValue.set('');
    this.addInputError.set(null);
    queueMicrotask(() => this.addInput?.nativeElement.focus());
  }

  protected removeCustom(value: number, event: MouseEvent): void {
    event.stopPropagation();
    const key = this.paletteKey;
    const wasSelected = this.selected().includes(value);
    this.customLevels.remove(key, value);
    if (wasSelected) {
      const next = this.selected().filter(v => v !== value);
      this.selected.set(next);
      this.valueChange.emit(next);
    }
    const ref = this.snackBar.open(this.translate.instant('RAIDS.LEVEL.REMOVED', { value }), this.translate.instant('COMMON.UNDO'), {
      duration: 3000,
    });
    ref.onAction().subscribe(() => {
      this.customLevels.add(key, value);
      if (wasSelected) {
        const next = [...this.selected(), value];
        this.selected.set(next);
        this.valueChange.emit(next);
      }
    });
  }

  protected toggle(value: number): void {
    const current = this.selected();
    let next: number[];
    if (this.multiple) {
      next = current.includes(value) ? current.filter(v => v !== value) : [...current, value];
    } else {
      next = current.includes(value) && current.length === 1 ? [] : [value];
    }
    this.selected.set(next);
    this.valueChange.emit(next);
  }

  private closeAdd(): void {
    this.addMode.set('closed');
    this.addInputValue.set('');
    this.addInputError.set(null);
  }

  private flash(value: number): void {
    this.flashValue.set(value);
    setTimeout(() => {
      if (this.flashValue() === value) this.flashValue.set(null);
    }, 600);
  }
}
