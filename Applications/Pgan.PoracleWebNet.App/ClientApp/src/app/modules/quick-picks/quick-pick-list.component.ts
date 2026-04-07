import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { QuickPickSummary } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { QuickPickService } from '../../core/services/quick-pick.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  imports: [
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatDialogModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTooltipModule,
  ],
  selector: 'app-quick-pick-list',
  standalone: true,
  styleUrl: './quick-pick-list.component.scss',
  templateUrl: './quick-pick-list.component.html',
})
export class QuickPickListComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly quickPickService = inject(QuickPickService);
  private readonly snackBar = inject(MatSnackBar);

  readonly alarmTypeColors: Record<string, string> = {
    raid: '#f44336',
    egg: '#ff9800',
    gym: '#00bcd4',
    invasion: '#607d8b',
    lure: '#e91e63',
    maxbattle: '#ff5722',
    monster: '#4caf50',
    nest: '#8bc34a',
    quest: '#9c27b0',
  };

  readonly picks = signal<QuickPickSummary[]>([]);

  readonly categories = computed(() => {
    const cats = new Set(this.picks().map(p => p.definition.category));
    return ['All', ...Array.from(cats).sort()];
  });

  readonly selectedCategory = signal<string | null>(null);

  readonly filteredPicks = computed(() => {
    const cat = this.selectedCategory();
    const all = this.picks();
    if (!cat || cat === 'All') return all;
    return all.filter(p => p.definition.category === cat);
  });

  readonly groupedPicks = computed(() => {
    const picks = this.filteredPicks();
    const groups = new Map<string, QuickPickSummary[]>();
    for (const pick of picks) {
      const cat = pick.definition.category || 'Other';
      if (!groups.has(cat)) groups.set(cat, []);
      groups.get(cat)!.push(pick);
    }
    return [...groups.entries()].map(([category, items]) => ({ category, items }));
  });

  readonly isAdmin = computed(() => this.auth.isAdmin());

  readonly loading = signal(true);

  readonly removing = signal<string | null>(null);

  loadPicks(autoSeed = true): void {
    this.loading.set(true);
    this.quickPickService.getAll().subscribe({
      error: () => {
        this.snackBar.open('Failed to load quick picks', 'OK', {
          duration: 3000,
        });
        this.loading.set(false);
      },
      next: picks => {
        if (picks.length === 0 && autoSeed && this.isAdmin()) {
          // First visit with no picks — seed defaults and reload
          this.quickPickService.seed().subscribe({
            error: () => this.loading.set(false),
            next: () => this.loadPicks(false),
          });
          return;
        }
        this.picks.set(picks.sort((a, b) => a.definition.sortOrder - b.definition.sortOrder));
        this.loading.set(false);
      },
    });
  }

  ngOnInit(): void {
    this.loadPicks();
  }

  onAddNew(): void {
    import('./quick-pick-admin-dialog.component').then(m => {
      const ref = this.dialog.open(m.QuickPickAdminDialogComponent, {
        width: '600px',
      });
      ref.afterClosed().subscribe(result => {
        if (result) this.loadPicks();
      });
    });
  }

  onApply(pick: QuickPickSummary): void {
    import('./quick-pick-apply-dialog.component').then(m => {
      const ref = this.dialog.open(m.QuickPickApplyDialogComponent, {
        width: '560px',
        data: pick,
      });
      ref.afterClosed().subscribe(result => {
        if (result) this.loadPicks();
      });
    });
  }

  onDelete(pick: QuickPickSummary): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        message: `Permanently delete "${pick.definition.name}"?`,
        title: 'Delete Quick Pick',
      },
    });
    ref.afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      const obs =
        pick.definition.scope === 'global'
          ? this.quickPickService.deleteAdmin(pick.definition.id)
          : this.quickPickService.deleteUser(pick.definition.id);
      obs.subscribe({
        error: () => this.snackBar.open('Failed to delete', 'OK', { duration: 3000 }),
        next: () => {
          this.snackBar.open('Quick pick deleted', 'OK', { duration: 3000 });
          this.loadPicks();
        },
      });
    });
  }

  onEdit(pick: QuickPickSummary): void {
    import('./quick-pick-admin-dialog.component').then(m => {
      const ref = this.dialog.open(m.QuickPickAdminDialogComponent, {
        width: '600px',
        data: pick.definition,
      });
      ref.afterClosed().subscribe(result => {
        if (result) this.loadPicks();
      });
    });
  }

  onRemove(pick: QuickPickSummary): void {
    const count = pick.appliedState?.trackedUids?.length ?? 0;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        message: `Remove "${pick.definition.name}"? This will delete ${count} alarm(s) created by this quick pick.${count > 10 ? ' This may take a moment.' : ''}`,
        title: 'Remove Quick Pick',
      },
    });
    ref.afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      this.removing.set(pick.definition.id);
      this.quickPickService.remove(pick.definition.id).subscribe({
        error: () => {
          this.snackBar.open('Failed to remove', 'OK', { duration: 3000 });
          this.removing.set(null);
        },
        next: () => {
          this.snackBar.open(`Quick pick removed — ${count} alarm(s) deleted`, 'OK', { duration: 3000 });
          this.removing.set(null);
          this.loadPicks();
        },
      });
    });
  }

  onReseed(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        message: 'This will delete all admin quick picks and re-create the defaults. User picks are not affected.',
        title: 'Reset to Defaults',
      },
    });
    ref.afterClosed().subscribe(confirmed => {
      if (!confirmed) return;
      this.loading.set(true);
      this.quickPickService.seed().subscribe({
        error: () => {
          this.snackBar.open('Failed to re-seed defaults', 'OK', { duration: 3000 });
          this.loading.set(false);
        },
        next: () => {
          this.snackBar.open('Defaults restored', 'OK', { duration: 3000 });
          this.loadPicks(false);
        },
      });
    });
  }

  selectCategory(cat: string): void {
    this.selectedCategory.set(cat === 'All' ? null : cat);
  }
}
