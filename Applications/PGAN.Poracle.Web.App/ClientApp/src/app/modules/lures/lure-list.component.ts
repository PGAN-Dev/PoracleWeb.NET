import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LureService } from '../../core/services/lure.service';
import { Lure } from '../../core/models';
import { LureAddDialogComponent } from './lure-add-dialog.component';
import { LureEditDialogComponent } from './lure-edit-dialog.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-lure-list', standalone: true, changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatButtonModule, MatIconModule, MatMenuModule, MatDialogModule, MatTooltipModule, MatSnackBarModule, MatProgressSpinnerModule],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Lure Alarms</h1>
        <p class="page-description">Track active lure modules: Normal, Glacial, Mossy, Magnetic, Rainy, or Golden.</p>
      </div>
      <div class="header-actions">
        <button mat-icon-button [matMenuTriggerFor]="bulkMenu" matTooltip="Bulk Actions"><mat-icon>more_vert</mat-icon></button>
        <mat-menu #bulkMenu="matMenu"><button mat-menu-item (click)="deleteAll()"><mat-icon color="warn">delete_sweep</mat-icon> Delete All</button></mat-menu>
        <button mat-fab color="primary" (click)="openAddDialog()" matTooltip="Add Lure Alarm"><mat-icon>add</mat-icon></button>
      </div>
    </div>
    @if (loading()) { <div class="loading-container"><mat-spinner diameter="48"></mat-spinner><p class="loading-text">Loading alarms...</p></div> }
    @else {
      <div class="alarm-grid">
        @for (lure of lures(); track lure.uid) {
          <mat-card class="alarm-card">
            <div class="card-top" [style.border-top-color]="getLureColor(lure.lureId)">
              <span class="lure-dot" [style.background]="getLureColor(lure.lureId)"></span>
              <div class="item-info"><h3>{{ getLureName(lure.lureId) }} Lure</h3><span class="lure-id">ID: {{ lure.lureId }}</span></div>
              <div class="card-top-actions">
                @if (lure.clean === 1) { <span class="clean-dot" matTooltip="Clean mode enabled"></span> }
                @if (lure.template) { <span class="template-chip" matTooltip="Template: {{ lure.template }}">{{ lure.template }}</span> }
              </div>
            </div>
            <mat-card-content>
              <div class="stat-grid"><div class="stat"><span class="stat-label">Type</span><span class="stat-value" [style.color]="getLureColor(lure.lureId)">{{ getLureName(lure.lureId) }}</span></div></div>
              <div class="distance-chip-row">
                @if (lure.distance === 0) { <span class="distance-chip area-mode"><mat-icon>map</mat-icon> Using Areas</span> }
                @else { <span class="distance-chip distance-mode"><mat-icon>straighten</mat-icon> {{ formatDistance(lure.distance) }}</span> }
              </div>
              @if (lure.ping) { <div class="ping-info"><mat-icon>notifications</mat-icon><span>{{ lure.ping }}</span></div> }
            </mat-card-content>
            <mat-card-actions align="end">
              <button mat-icon-button (click)="editLure(lure)" matTooltip="Edit"><mat-icon>edit</mat-icon></button>
              <button mat-icon-button (click)="deleteLure(lure)" matTooltip="Delete" color="warn"><mat-icon>delete</mat-icon></button>
            </mat-card-actions>
          </mat-card>
        } @empty {
          <div class="empty-state"><mat-icon class="empty-icon">location_on</mat-icon><h2>No Lure Alarms Configured</h2><p>Tap + to add your first lure alarm</p>
            <button mat-fab extended color="primary" (click)="openAddDialog()"><mat-icon>add</mat-icon> Add Lure</button></div>
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
    .card-top { display: flex; align-items: center; gap: 12px; padding: 16px 16px 0; border-top: 4px solid #FF9800; }
    .card-top-actions { margin-left: auto; display: flex; align-items: center; gap: 6px; }
    .clean-dot { width: 10px; height: 10px; border-radius: 50%; background: #4caf50; display: inline-block; }
    .template-chip { display: inline-block; background: #e8eaf6; color: #3f51b5; padding: 2px 8px; border-radius: 12px; font-size: 10px; font-weight: 500; max-width: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lure-dot { width: 32px; height: 32px; border-radius: 50%; display: inline-block; flex-shrink: 0; }
    .item-info h3 { margin: 0; font-size: 18px; } .lure-id { color: rgba(0,0,0,0.54); font-size: 13px; }
    .stat-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 8px; }
    .stat { display: flex; flex-direction: column; }
    .stat-label { font-size: 11px; color: rgba(0,0,0,0.54); text-transform: uppercase; letter-spacing: 0.5px; }
    .stat-value { font-size: 14px; font-weight: 500; }
    .distance-chip-row { margin-top: 12px; }
    .distance-chip { display: inline-flex; align-items: center; gap: 4px; padding: 4px 12px; border-radius: 16px; font-size: 12px; font-weight: 500; }
    .distance-chip mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .area-mode { background: #e8f5e9; color: #2e7d32; } .distance-mode { background: #e3f2fd; color: #1565c0; }
    .ping-info { display: flex; align-items: center; gap: 4px; margin-top: 8px; font-size: 13px; color: rgba(0,0,0,0.64); }
    .ping-info mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .empty-state { grid-column: 1 / -1; text-align: center; padding: 64px 16px; }
    .empty-icon { font-size: 64px; width: 64px; height: 64px; color: rgba(0,0,0,0.24); }
    .empty-state h2 { margin: 16px 0 8px; font-weight: 400; } .empty-state p { color: rgba(0,0,0,0.54); margin-bottom: 24px; }
  `],
})
export class LureListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly lureService = inject(LureService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  readonly lures = signal<Lure[]>([]); readonly loading = signal(true);

  ngOnInit(): void { this.loadLures(); }
  loadLures(): void { this.loading.set(true); this.lureService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: l => { this.lures.set(l); this.loading.set(false); }, error: () => this.loading.set(false) }); }
  getLureName(id: number): string { switch(id) { case 501: return 'Normal'; case 502: return 'Glacial'; case 503: return 'Mossy'; case 504: return 'Magnetic'; case 505: return 'Rainy'; case 506: return 'Golden'; default: return `Lure #${id}`; } }
  getLureColor(id: number): string { switch(id) { case 501: return '#FF9800'; case 502: return '#03A9F4'; case 503: return '#4CAF50'; case 504: return '#9E9E9E'; case 505: return '#2196F3'; case 506: return '#FFC107'; default: return '#9E9E9E'; } }
  formatDistance(meters: number): string { return meters >= 1000 ? `${(meters/1000).toFixed(1)} km` : `${meters} m`; }
  openAddDialog(): void { this.dialog.open(LureAddDialogComponent, { width: '600px', maxHeight: '90vh' }).afterClosed().subscribe(r => { if (r) this.loadLures(); }); }
  editLure(lure: Lure): void { this.dialog.open(LureEditDialogComponent, { width: '600px', maxHeight: '90vh', data: lure }).afterClosed().subscribe(r => { if (r) this.loadLures(); }); }
  deleteLure(lure: Lure): void {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete Lure Alarm', message: `Delete the ${this.getLureName(lure.lureId)} lure alarm?`, confirmText: 'Delete', warn: true } as ConfirmDialogData })
      .afterClosed().subscribe(c => { if (c) this.lureService.delete(lure.uid).subscribe({ next: () => { this.snackBar.open('Lure alarm deleted', 'OK', { duration: 3000 }); this.loadLures(); }, error: () => this.snackBar.open('Failed to delete alarm', 'OK', { duration: 3000 }) }); });
  }
  deleteAll(): void {
    this.dialog.open(ConfirmDialogComponent, { data: { title: 'Delete All Lure Alarms', message: 'Delete ALL lure alarms? This cannot be undone.', confirmText: 'Delete All', warn: true } as ConfirmDialogData })
      .afterClosed().subscribe(c => { if (c) this.lureService.deleteAll().subscribe({ next: () => { this.snackBar.open('All lure alarms deleted', 'OK', { duration: 3000 }); this.loadLures(); }, error: () => this.snackBar.open('Failed to delete alarms', 'OK', { duration: 3000 }) }); });
  }
}
