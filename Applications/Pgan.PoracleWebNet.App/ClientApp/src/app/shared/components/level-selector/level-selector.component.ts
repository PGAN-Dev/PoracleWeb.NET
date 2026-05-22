import { Component, computed, DestroyRef, ElementRef, EventEmitter, inject, Input, OnInit, Output, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { ANY_LEVEL, ANY_LEVEL_VALUE, isKnownLevel, LevelOption, makeCustomLevel } from '../../../core/models/raid-level.models';
import { RaidLevelService } from '../../../core/services/raid-level.service';

/**
 * Chip-based selector for raid/egg/boss levels. Standard star tiers + Mega
 * always render as a primary row; the additional Pokémon GO raid types
 * (Ultra Beast, Elite, Primal, Shadow, Super Mega, Coordinated) live in a
 * "More raid types…" overflow menu so the dialog stays compact.
 *
 * Custom integers typed via the `+ Add` chip live in the component's local
 * state for the dialog session and are seeded from whatever was passed in
 * via `[value]`. They are NOT persisted across dialog opens — close the
 * dialog and the typed-but-not-saved chips are gone.
 *
 * `pickerType` determines what the component shows and how it behaves:
 * - `raid`  : multi-select, primary + overflow, Any chip
 * - `egg`   : multi-select, primary only (no overflow), no Any
 * - `boss`  : single-select, primary + overflow, Any chip
 */
@Component({
  imports: [MatButtonModule, MatChipsModule, MatIconModule, MatMenuModule, MatSnackBarModule, MatTooltipModule, TranslateModule],
  selector: 'app-level-selector',
  standalone: true,
  styleUrl: './level-selector.component.scss',
  templateUrl: './level-selector.component.html',
})
export class LevelSelectorComponent implements OnInit {
  /** Explicit two-state machine for the add affordance. */
  private readonly addMode = signal<'closed' | 'open'>('closed');
  /**
   * Custom palette — chips for integers not in the canonical 1-19 list.
   * Ephemeral: lives only for the lifetime of this component instance.
   * Closing the dialog destroys the component and the palette with it.
   */
  private readonly customPalette = signal<number[]>([]);
  private readonly destroyRef = inject(DestroyRef);
  private readonly raidLevelService = inject(RaidLevelService);

  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  @ViewChild('addInput') addInput?: ElementRef<HTMLInputElement>;
  protected readonly addInputError = signal<string | null>(null);
  protected readonly addInputValue = signal('');
  protected readonly anyLevel = ANY_LEVEL;

  protected readonly flashValue = signal<number | null>(null);

  /** Which kind of picker this instance is. Drives layout + behavior. */
  @Input({ required: true }) pickerType!: 'raid' | 'egg' | 'boss';
  /** Levels relegated to the "More raid types…" overflow menu. Empty for eggs. */
  protected readonly overflowLevels = computed<LevelOption[]>(() => {
    if (this.pickerType === 'egg') return [];
    return this.raidLevelService.levels().filter(l => l.category !== 'star' && l.category !== 'mega');
  });

  /** Internal selection state, mirrored from `[value]` input. */
  protected readonly selected = signal<number[]>([]);

  protected readonly hasOverflowSelected = computed(() => {
    const sel = new Set(this.selected());
    return this.overflowLevels().some(l => sel.has(l.value));
  });

  protected isAddClosed = () => this.addMode() === 'closed';
  protected isAddOpen = () => this.addMode() === 'open';

  protected readonly palette = computed<LevelOption[]>(() => this.customPalette().map(makeCustomLevel));
  /** Levels shown in the primary chip row. Driven by pickerType + live raid-level list. */
  protected readonly primaryLevels = computed<LevelOption[]>(() => {
    const all = this.raidLevelService.levels();
    if (this.pickerType === 'egg') {
      return all.filter(l => l.category === 'star');
    }
    return all.filter(l => l.category === 'star' || l.category === 'mega');
  });

  protected readonly selectedOverflowChips = computed(() => {
    const sel = new Set(this.selected());
    return this.overflowLevels().filter(l => sel.has(l.value));
  });

  @Output() readonly valueChange = new EventEmitter<number[]>();

  protected get multiple(): boolean {
    return this.pickerType !== 'boss';
  }

  protected get showAny(): boolean {
    return this.pickerType !== 'egg';
  }

  @Input()
  set value(next: number[] | null | undefined) {
    const safe = (next ?? []).filter(v => Number.isInteger(v) && v >= 1);
    this.selected.set(safe);
    // Seed the local palette from any custom values on the incoming alarm so
    // the chips show pre-selected. Built-in levels (1-19) already render as
    // primary/overflow chips; only the truly unknown integers need a custom chip.
    const customs = safe.filter(v => !isKnownLevel(v));
    if (customs.length > 0) {
      this.customPalette.update(current => {
        const seen = new Set(current);
        const next2 = [...current];
        for (const v of customs) {
          if (!seen.has(v)) {
            seen.add(v);
            next2.push(v);
          }
        }
        return next2;
      });
    }
  }

  protected cancelAddInput(): void {
    this.closeAdd();
  }

  protected commitAddInput(): void {
    const raw = this.addInputValue().trim();
    if (raw === '') {
      this.closeAdd();
      return;
    }
    const parsed = Number.parseInt(raw, 10);
    if (!Number.isInteger(parsed) || parsed < 1 || String(parsed) !== raw.replace(/^0+(\d)/, '$1')) {
      this.addInputError.set('RAIDS.LEVEL.INVALID');
      return;
    }

    // Snap 9000 to the dedicated Any chip when surfaced.
    if (parsed === ANY_LEVEL_VALUE && this.showAny) {
      this.closeAdd();
      if (!this.isSelected(ANY_LEVEL_VALUE)) this.toggle(ANY_LEVEL_VALUE);
      this.flash(ANY_LEVEL_VALUE);
      return;
    }

    // Duplicate of a known level — just select that chip.
    if (isKnownLevel(parsed)) {
      this.closeAdd();
      if (!this.isSelected(parsed)) this.toggle(parsed);
      this.flash(parsed);
      return;
    }

    // Duplicate of an existing custom chip — flash + select.
    if (this.customPalette().includes(parsed)) {
      this.addInputError.set(this.translate.instant('RAIDS.LEVEL.DUPLICATE', { value: parsed }));
      this.flash(parsed);
      if (!this.isSelected(parsed)) this.toggle(parsed);
      return;
    }

    this.customPalette.update(current => [...current, parsed]);
    this.closeAdd();
    if (!this.isSelected(parsed)) this.toggle(parsed);
  }

  protected isSelected(value: number): boolean {
    return this.selected().includes(value);
  }

  ngOnInit(): void {
    this.raidLevelService.load();
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
    const wasSelected = this.selected().includes(value);
    this.customPalette.update(current => current.filter(v => v !== value));
    if (wasSelected) {
      const next = this.selected().filter(v => v !== value);
      this.selected.set(next);
      this.valueChange.emit(next);
    }
    const ref = this.snackBar.open(this.translate.instant('RAIDS.LEVEL.REMOVED', { value }), this.translate.instant('COMMON.UNDO'), {
      duration: 3000,
    });
    ref
      .onAction()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.customPalette.update(current => (current.includes(value) ? current : [...current, value]));
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

  protected toggleFromOverflow(value: number): void {
    this.toggle(value);
    this.flash(value);
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
