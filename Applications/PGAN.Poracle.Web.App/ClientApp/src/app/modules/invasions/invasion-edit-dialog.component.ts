import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { InvasionService } from '../../core/services/invasion.service';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { Invasion, InvasionUpdate } from '../../core/models';

@Component({
  selector: 'app-invasion-edit-dialog', standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, MatIconModule, MatRadioModule, MatTabsModule, MatSnackBarModule, TemplateSelectorComponent, DeliveryPreviewComponent],
  template: `
    <h2 mat-dialog-title>Edit Invasion Alarm</h2>
    <mat-dialog-content>
      <div class="item-header"><mat-icon class="rocket-icon">security</mat-icon><div><h3>{{ data.gruntType || 'Unknown Grunt' }}</h3><span class="item-subtitle">{{ getGenderLabel() }}</span></div></div>

      <mat-tab-group animationDuration="200ms" class="alarm-tabs">
        <!-- Tab 1: Settings -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>tune</mat-icon>
            <span class="tab-label">Settings</span>
          </ng-template>
          <div class="tab-content">
            <mat-form-field appearance="outline" class="full-width"><mat-label>Gender</mat-label>
              <mat-select [formControl]="form.controls.gender"><mat-option [value]="0">Any</mat-option><mat-option [value]="1">Male</mat-option><mat-option [value]="2">Female</mat-option></mat-select>
            </mat-form-field>
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
          </div>
        </mat-tab>

        <!-- Tab 2: Delivery -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>notifications</mat-icon>
            <span class="tab-label">Delivery</span>
          </ng-template>
          <div class="tab-content">
            <mat-form-field appearance="outline" class="full-width"><mat-label>Ping / Role</mat-label><input matInput [formControl]="form.controls.ping" /></mat-form-field>
            <app-template-selector [alarmType]="'invasion'" [value]="form.controls.template.value ?? ''" (valueChange)="form.controls.template.setValue($event)"></app-template-selector>
            <mat-slide-toggle [formControl]="form.controls.clean">Clean mode</mat-slide-toggle>
          </div>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancel</button>
      <button mat-raised-button color="primary" (click)="save()" [disabled]="saving()">{{ saving() ? 'Saving...' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 400px; max-width: 600px; }
    .alarm-tabs { margin: 0 -24px; }
    :host ::ng-deep .alarm-tabs .mat-mdc-tab-body-wrapper { padding: 0 24px; }
    .tab-content { padding: 16px 0; }
    .tab-label { margin-left: 6px; }
    .item-header { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }
    .rocket-icon { font-size: 48px; width: 48px; height: 48px; color: #424242; }
    .item-header h3 { margin: 0; } .item-subtitle { color: rgba(0,0,0,0.54); font-size: 13px; }
    .full-width { width: 100%; } h4 { margin: 16px 0 8px; color: rgba(0,0,0,0.64); }
    mat-slide-toggle { margin: 16px 0; }
    .distance-radio-group { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .radio-label { display: flex; align-items: flex-start; gap: 8px; }
    .radio-label mat-icon { margin-top: 2px; color: rgba(0,0,0,0.54); }
    .radio-hint { margin: 2px 0 0; font-size: 12px; color: rgba(0,0,0,0.54); font-weight: normal; }
  `],
})
export class InvasionEditDialogComponent {
  readonly dialogRef = inject(MatDialogRef<InvasionEditDialogComponent>);
  readonly data = inject<Invasion>(MAT_DIALOG_DATA);
  private readonly invasionService = inject(InvasionService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  saving = signal(false);
  form = this.fb.group({
    gender: [this.data.gender], distanceMode: [this.data.distance === 0 ? 'areas' : 'distance' as 'areas' | 'distance'],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    ping: [this.data.ping ?? ''], template: [this.data.template ?? ''], clean: [this.data.clean === 1],
  });
  getGenderLabel(): string { switch (this.data.gender) { case 1: return 'Male'; case 2: return 'Female'; default: return 'Any Gender'; } }
  onDistanceModeChange(): void { if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0); else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1); }
  save(): void {
    this.saving.set(true); const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.invasionService.update(this.data.uid, { gender: v.gender ?? 0, distance: dist, ping: v.ping || null, template: v.template || null, clean: v.clean ? 1 : 0 } as InvasionUpdate).subscribe({
      next: () => { this.snackBar.open('Invasion alarm updated', 'OK', { duration: 3000 }); this.dialogRef.close(true); },
      error: () => { this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 }); this.saving.set(false); },
    });
  }
}
