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
  readonly canSave = computed(() => this.name().trim().length > 0 && !!this.normalizedBase() && !this.duplicate() && this.probeOk());

  readonly dialogRef = inject(MatDialogRef<IconRepoDialogComponent, IconRepo>);

  readonly editing = this.data.repo !== null;

  readonly previews = ICON_REPO_PREVIEWS;

  readonly probeState = signal<ProbeState>({ kind: 'idle' });

  constructor() {
    if (this.data.repo) {
      this.name.set(this.data.repo.name);
      this.base.set(this.data.repo.base);
      // An entry already in the list was probed when it was added. Re-probing on open would block
      // editing a typo in the name while the pack's host happens to be down.
      this.probeState.set({ kind: 'checked', missing: [] });
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
