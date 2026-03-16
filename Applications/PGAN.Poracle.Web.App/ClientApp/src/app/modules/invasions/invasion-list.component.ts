import { Component, OnInit, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { Invasion } from '../../core/models';
import { InvasionAddDialogComponent } from './invasion-add-dialog.component';
import { InvasionEditDialogComponent } from './invasion-edit-dialog.component';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-invasion-list',
  standalone: true,
  imports: [
    MatCardModule, MatButtonModule, MatIconModule, MatMenuModule,
    MatDialogModule, MatTooltipModule, MatSnackBarModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Invasion Alarms</h1>
        <p class="page-description">Get alerts for Team GO Rocket grunt encounters by type.</p>
      </div>
      <div class="header-actions">
        <button mat-icon-button [matMenuTriggerFor]="bulkMenu" matTooltip="Bulk Actions"><mat-icon>more_vert</mat-icon></button>
        <mat-menu #bulkMenu="matMenu">
          <button mat-menu-item (click)="deleteAll()"><mat-icon color="warn">delete_sweep</mat-icon> Delete All</button>
        </mat-menu>
        <button mat-fab color="primary" (click)="openAddDialog()" matTooltip="Add Invasion Alarm"><mat-icon>add</mat-icon></button>
      </div>
    </div>
    @if (loading()) {
      <div class="loading-container"><mat-spinner diameter="48"></mat-spinner><p class="loading-text">Loading alarms...</p></div>
    } @else {
      <div class="alarm-grid">
        @for (invasion of invasions(); track invasion.uid) {
          <mat-card class="alarm-card">
            <div class="card-top">
              <mat-icon class="rocket-icon">security</mat-icon>
              <div class="item-info">
                <h3>{{ invasion.gruntType || 'Unknown Grunt' }}</h3>
                <span class="gender-label">
                  @if (invasion.gender === 1) { <mat-icon class="gender-icon">male</mat-icon> Male }
                  @else if (invasion.gender === 2) { <mat-icon class="gender-icon">female</mat-icon> Female }
                  @else { Any Gender }
                </span>
              </div>
              <div class="card-top-actions">
                @if (invasion.clean === 1) { <span class="clean-dot" matTooltip="Clean mode enabled"></span> }
                @if (invasion.template) { <span class="template-chip" matTooltip="Template: {{ invasion.template }}">{{ invasion.template }}</span> }
              </div>
            </div>
            <mat-card-content>
              <div class="distance-chip-row">
                @if (invasion.distance === 0) {
                  <span class="distance-chip area-mode"><mat-icon>map</mat-icon> Using Areas</span>
                } @else {
                  <span class="distance-chip distance-mode"><mat-icon>straighten</mat-icon> {{ formatDistance(invasion.distance) }}</span>
                }
              </div>
              @if (invasion.ping) { <div class="ping-info"><mat-icon>notifications</mat-icon><span>{{ invasion.ping }}</span></div> }
            </mat-card-content>
            <mat-card-actions align="end">
              <button mat-icon-button (click)="editInvasion(invasion)" matTooltip="Edit"><mat-icon>edit</mat-icon></button>
              <button mat-icon-button (click)="deleteInvasion(invasion)" matTooltip="Delete" color="warn"><mat-icon>delete</mat-icon></button>
            </mat-card-actions>
          </mat-card>
        } @empty {
          <div class="empty-state">
            <mat-icon class="empty-icon">security</mat-icon>
            <h2>No Invasion Alarms Configured</h2>
            <p>Tap + to add your first invasion alarm</p>
            <button mat-fab extended color="primary" (click)="openAddDialog()"><mat-icon>add</mat-icon> Add Invasion</button>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; padding: 16px 24px; gap: 16px; }
    .page-header-text { flex: 1; min-width: 0; }
    .page-header h1 { margin: 0; font-size: 24px; font-weight: 400; }
    .page-description { margin: 4px 0 0; color: var(--text-secondary, rgba(0,0,0,0.54)); font-size: 13px; line-height: 1.5; border-left: 3px solid #1976d2; padding-left: 12px; }
    .header-actions { display: flex; align-items: center; gap: 8px; }
    .loading-container { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 64px; gap: 16px; }
    .loading-text { color: rgba(0,0,0,0.54); font-size: 14px; }
    .alarm-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; padding: 0 24px 24px; }
    .alarm-card { position: relative; transition: transform 0.2s, box-shadow 0.2s; }
    .alarm-card:hover { transform: translateY(-2px); box-shadow: 0 4px 16px rgba(0,0,0,0.12); }
    .card-top { display: flex; align-items: center; gap: 12px; padding: 16px 16px 0; border-top: 4px solid #424242; }
    .card-top-actions { margin-left: auto; display: flex; align-items: center; gap: 6px; }
    .clean-dot { width: 10px; height: 10px; border-radius: 50%; background: #4caf50; display: inline-block; }
    .template-chip { display: inline-block; background: #e8eaf6; color: #3f51b5; padding: 2px 8px; border-radius: 12px; font-size: 10px; font-weight: 500; max-width: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .rocket-icon { font-size: 48px; width: 48px; height: 48px; color: #424242; }
    .item-info h3 { margin: 0; font-size: 18px; }
    .gender-label { display: flex; align-items: center; gap: 4px; color: rgba(0,0,0,0.54); font-size: 13px; }
    .gender-icon { font-size: 16px; width: 16px; height: 16px; }
    .distance-chip-row { margin-top: 12px; }
    .distance-chip { display: inline-flex; align-items: center; gap: 4px; padding: 4px 12px; border-radius: 16px; font-size: 12px; font-weight: 500; }
    .distance-chip mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .area-mode { background: #e8f5e9; color: #2e7d32; }
    .distance-mode { background: #e3f2fd; color: #1565c0; }
    .ping-info { display: flex; align-items: center; gap: 4px; margin-top: 8px; font-size: 13px; color: rgba(0,0,0,0.64); }
    .ping-info mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .empty-state { grid-column: 1 / -1; text-align: center; padding: 64px 16px; }
    .empty-icon { font-size: 64px; width: 64px; height: 64px; color: rgba(0,0,0,0.24); }
    .empty-state h2 { margin: 16px 0 8px; font-weight: 400; }
    .empty-state p { color: rgba(0,0,0,0.54); margin-bottom: 24px; }
  `],
})
export class InvasionListComponent implements OnInit {
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  readonly invasions = signal<Invasion[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void { this.masterData.loadData().subscribe(); this.loadInvasions(); }

  loadInvasions(): void {
    this.loading.set(true);
    this.invasionService.getAll().subscribe({ next: (inv) => { this.invasions.set(inv); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  formatDistance(meters: number): string { return meters >= 1000 ? `${(meters/1000).toFixed(1)} km` : `${meters} m`; }

  openAddDialog(): void { this.dialog.open(InvasionAddDialogComponent, { width: '600px', maxHeight: '90vh' }).afterClosed().subscribe(r => { if (r) this.loadInvasions(); }); }
  editInvasion(invasion: Invasion): void { this.dialog.open(InvasionEditDialogComponent, { width: '600px', maxHeight: '90vh', data: invasion }).afterClosed().subscribe(r => { if (r) this.loadInvasions(); }); }

  deleteInvasion(invasion: Invasion): void {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete Invasion Alarm', message: `Delete alarm for ${invasion.gruntType || 'this grunt'}?`, confirmText: 'Delete', warn: true } as ConfirmDialogData })
      .afterClosed().subscribe(c => { if (c) this.invasionService.delete(invasion.uid).subscribe({ next: () => { this.snackBar.open('Invasion alarm deleted', 'OK', { duration: 3000 }); this.loadInvasions(); }, error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }) }); });
  }

  deleteAll(): void {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete All Invasion Alarms', message: 'Delete ALL invasion alarms? This cannot be undone.', confirmText: 'Delete All', warn: true } as ConfirmDialogData })
      .afterClosed().subscribe(c => { if (c) this.invasionService.deleteAll().subscribe({ next: () => { this.snackBar.open('All invasion alarms deleted', 'OK', { duration: 3000 }); this.loadInvasions(); }, error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }) }); });
  }
}
