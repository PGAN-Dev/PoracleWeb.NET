import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';

import { IconPackProbeService } from '../../../core/services/icon-pack-probe.service';
import { IconSourceKey } from '../../../core/services/icon.service';
import { ICON_REPO_PREVIEWS, IconRepo, isValidRepoBase, normalizeRepoBase } from '../../../shared/utils/icon-repos';

export interface IconRepoDialogData {
  /** Bases already in the list, so the dialog can refuse a duplicate before the operator saves one. */
  existingBases: string[];
  /** The entry being edited, or null when adding. */
  repo: IconRepo | null;
}

/** What a probe of the entered URL last said. */
type ProbeState = { kind: 'checked'; missing: IconSourceKey[] } | { kind: 'checking' } | { kind: 'idle' };

@Component({
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslatePipe,
  ],
  selector: 'app-icon-repo-dialog',
  standalone: true,
  styleUrl: './icon-repo-dialog.component.scss',
  templateUrl: './icon-repo-dialog.component.html',
})
export class IconRepoDialogComponent {
  private readonly data = inject<IconRepoDialogData>(MAT_DIALOG_DATA);
  private readonly probeService = inject(IconPackProbeService);
  readonly base = signal('');
  /** The normalized base, or empty while it is not a usable URL. */
  readonly normalizedBase = computed(() => (isValidRepoBase(this.base()) ? normalizeRepoBase(this.base()) : ''));
  /**
   * A base already in the list, excluding the entry being edited. Two cards pointing at one pack is
   * not harmful, just confusing: whichever is clicked writes the same six settings, so the other
   * would also light up as active.
   */
  readonly duplicate = computed(() => {
    const candidate = this.normalizedBase();
    if (!candidate) return false;
    return this.data.existingBases.some(b => b === candidate && b !== this.data.repo?.base);
  });

  readonly name = signal('');

  /**
   * Whether this is a genuine edit of an entry already in the list, rather than an add.
   *
   * Derived from the list instead of trusted from a flag, because the two paths reach this dialog
   * through the same call: the pack-card pencil passes a listed entry, and the unlisted "currently
   * configured" card's *Add to list* passes one that is not in the list at all. Treating the second
   * as an edit is what let an unchecked pack through -- see the note on `canSave`.
   */
  readonly listedEdit = this.data.repo !== null && this.data.existingBases.includes(this.data.repo.base);

  /**
   * An entry already in the list, whose URL has not been touched. Renaming one is allowed without a
   * fresh check -- that is the case worth protecting, because a pack whose host is down for an hour
   * should not also block fixing a typo in its name.
   *
   * It has to be both halves. This started as "editing, so assume checked", which was seeded into
   * `probeState` in the constructor, and the dialog then printed *Every category loaded* about a
   * pack nothing had ever loaded. On the unlisted card that meant claiming a deleted repository was
   * fine -- the precise failure this check exists to catch.
   */
  readonly unchangedListedEntry = computed(() => this.listedEdit && this.normalizedBase() === this.data.repo?.base);

  readonly canSave = computed(
    () => this.name().trim().length > 0 && !!this.normalizedBase() && !this.duplicate() && (this.probeOk() || this.unchangedListedEntry()),
  );

  readonly dialogRef = inject(MatDialogRef<IconRepoDialogComponent, IconRepo>);

  readonly editing = this.listedEdit;

  readonly previews = ICON_REPO_PREVIEWS;

  readonly probeState = signal<ProbeState>({ kind: 'idle' });

  constructor() {
    if (this.data.repo) {
      this.base.set(this.data.repo.base);
      // Only a listed entry brings its name. The unlisted card's name is a label this page derived
      // from the URL for display; saving that as a pack name would put machine text in the menu.
      if (this.listedEdit) this.name.set(this.data.repo.name);
    }
  }

  async check(): Promise<void> {
    const candidate = this.normalizedBase();
    if (!candidate) return;

    this.probeState.set({ kind: 'checking' });
    const result = await this.probeService.probe(candidate);
    // The operator may have kept typing while the images loaded; a verdict about a URL that is no
    // longer in the box is worse than none.
    if (this.normalizedBase() !== result.base) return;
    this.probeState.set({ kind: 'checked', missing: result.missing });
  }

  /** The categories the last probe could not find, as the setting keys an operator would recognise. */
  missingKeys(): IconSourceKey[] {
    const state = this.probeState();
    return state.kind === 'checked' ? state.missing : [];
  }

  /** Discard a stale verdict the moment the URL changes, so Add cannot be pressed on the old one. */
  onBaseChanged(value: string): void {
    this.base.set(value);
    this.probeState.set({ kind: 'idle' });
  }

  previewUrl(path: string): string {
    return `${this.normalizedBase()}/${path}`;
  }

  probeOk(): boolean {
    const state = this.probeState();
    return state.kind === 'checked' && state.missing.length === 0;
  }

  save(): void {
    if (!this.canSave()) return;
    this.dialogRef.close({ name: this.name().trim(), base: this.normalizedBase() });
  }
}
