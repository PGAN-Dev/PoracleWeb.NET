import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LureService } from '../../core/services/lure.service';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { Lure, LureUpdate } from '../../core/models';

@Component({
  selector: 'app-lure-edit-dialog', standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatIconModule, MatRadioModule, MatSnackBarModule, TemplateSelectorComponent, DeliveryPreviewComponent],
  template: `
    <h2 mat-dialog-title>Edit Lure Alarm</h2>
    <mat-dialog-content>
      <div class="item-header"><span class="lure-dot" [style.background]="getLureColor(data.lureId)"></span><div><h3>{{ getLureName(data.lureId) }} Lure</h3></div></div>
      <h4>Location Mode</h4>
      <mat-radio-group [formControl]="form.controls.distanceMode" class="distance-radio-group" (change)="onDistanceModeChange()">
        <mat-radio-button value="areas"><div class="radio-label"><mat-icon>map</mat-icon><div><strong>Use Areas</strong><p class="radio-hint">Notifications will use your configured area geofences</p></div></div></mat-radio-button>
        <mat-radio-button value="distance"><div class="radio-label"><mat-icon>straighten</mat-icon><div><strong>Set Distance</strong><p class="radio-hint">Notify within a radius from your location</p></div></div></mat-radio-button>
      </mat-radio-group>
      @if (form.controls.distanceMode.value === 'distance') {
        <mat-form-field appearance="outline" class="full-width"><mat-label>Distance</mat-label><input matInput type="number" [formControl]="form.controls.distanceKm" min="0" step="0.1" /><span matSuffix>km</span></mat-form-field>
      }
      <app-delivery-preview
        [mode]="form.controls.distanceMode.value === 'areas' ? 'areas' : 'distance'"
        [distanceKm]="form.controls.distanceKm.value ?? 0">
      </app-delivery-preview>
      <mat-form-field appearance="outline" class="full-width"><mat-label>Ping / Role</mat-label><input matInput [formControl]="form.controls.ping" /></mat-form-field>
      <app-template-selector [alarmType]="'lure'" [value]="form.controls.template.value ?? ''" (valueChange)="form.controls.template.setValue($event)"></app-template-selector>
      <mat-slide-toggle [formControl]="form.controls.clean">Clean mode</mat-slide-toggle>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancel</button>
      <button mat-raised-button color="primary" (click)="save()" [disabled]="saving()">{{ saving() ? 'Saving...' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 400px; max-width: 600px; }
    .item-header { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }
    .lure-dot { width: 32px; height: 32px; border-radius: 50%; display: inline-block; }
    .item-header h3 { margin: 0; } .full-width { width: 100%; }
    h4 { margin: 16px 0 8px; color: rgba(0,0,0,0.64); } mat-slide-toggle { margin: 16px 0; }
    .distance-radio-group { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .radio-label { display: flex; align-items: flex-start; gap: 8px; }
    .radio-label mat-icon { margin-top: 2px; color: rgba(0,0,0,0.54); }
    .radio-hint { margin: 2px 0 0; font-size: 12px; color: rgba(0,0,0,0.54); font-weight: normal; }
  `],
})
export class LureEditDialogComponent {
  readonly dialogRef = inject(MatDialogRef<LureEditDialogComponent>);
  readonly data = inject<Lure>(MAT_DIALOG_DATA);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  saving = signal(false);
  form = this.fb.group({
    distanceMode: [this.data.distance === 0 ? 'areas' : 'distance' as 'areas' | 'distance'],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    ping: [this.data.ping ?? ''], template: [this.data.template ?? ''], clean: [this.data.clean === 1],
  });
  getLureName(id: number): string { switch(id) { case 501: return 'Normal'; case 502: return 'Glacial'; case 503: return 'Mossy'; case 504: return 'Magnetic'; case 505: return 'Rainy'; case 506: return 'Golden'; default: return `Lure #${id}`; } }
  getLureColor(id: number): string { switch(id) { case 501: return '#FF9800'; case 502: return '#03A9F4'; case 503: return '#4CAF50'; case 504: return '#9E9E9E'; case 505: return '#2196F3'; case 506: return '#FFC107'; default: return '#9E9E9E'; } }
  onDistanceModeChange(): void { if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0); else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1); }
  save(): void {
    this.saving.set(true); const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.lureService.update(this.data.uid, { distance: dist, ping: v.ping || null, template: v.template || null, clean: v.clean ? 1 : 0 } as LureUpdate).subscribe({
      next: () => { this.snackBar.open('Lure alarm updated', 'OK', { duration: 3000 }); this.dialogRef.close(true); },
      error: () => { this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 }); this.saving.set(false); },
    });
  }
}
