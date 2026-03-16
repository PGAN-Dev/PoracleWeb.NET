import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { GymService } from '../../core/services/gym.service';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { GymCreate } from '../../core/models';
import { forkJoin } from 'rxjs';

interface TeamOption { id: number; name: string; color: string; }

@Component({
  selector: 'app-gym-add-dialog', standalone: true,
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, MatIconModule, MatCheckboxModule, MatRadioModule, MatTabsModule, MatSnackBarModule, TemplateSelectorComponent, DeliveryPreviewComponent],
  template: `
    <h2 mat-dialog-title>Add Gym Alarm</h2>
    <mat-dialog-content>
      <mat-tab-group animationDuration="200ms" class="alarm-tabs">
        <!-- Tab 1: Teams -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>fitness_center</mat-icon>
            <span class="tab-label">Teams</span>
          </ng-template>
          <div class="tab-content">
            <h4>Teams</h4>
            <div class="team-grid">
              @for (team of teams; track team.id) {
                <mat-checkbox [checked]="selectedTeamIds().includes(team.id)" (change)="toggleTeam(team.id)">
                  <span class="team-label"><span class="team-dot" [style.background]="team.color"></span>{{ team.name }}</span>
                </mat-checkbox>
              }
            </div>
            @if (selectedTeamIds().length > 0) { <p class="selection-count">{{ selectedTeamIds().length }} team(s) selected</p> }
            <mat-slide-toggle [formControl]="form.controls.slot_changes">Track Slot Changes</mat-slide-toggle>
            <mat-slide-toggle [formControl]="form.controls.battle_changes">Track Battle Changes</mat-slide-toggle>
          </div>
        </mat-tab>

        <!-- Tab 2: Delivery -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>notifications</mat-icon>
            <span class="tab-label">Delivery</span>
          </ng-template>
          <div class="tab-content">
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
            <h4>Common Settings</h4>
            <mat-form-field appearance="outline" class="full-width"><mat-label>Ping / Role</mat-label><input matInput [formControl]="form.controls.ping" placeholder="e.g. @role or empty" /></mat-form-field>
            <app-template-selector [alarmType]="'gym'" [value]="form.controls.template.value ?? ''" (valueChange)="form.controls.template.setValue($event)"></app-template-selector>
            <mat-slide-toggle [formControl]="form.controls.clean">Clean mode (auto-delete)</mat-slide-toggle>
          </div>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancel</button>
      <button mat-raised-button color="primary" (click)="save()" [disabled]="saving() || selectedTeamIds().length === 0">{{ saving() ? 'Saving...' : 'Save' }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 400px; max-width: 600px; }
    .alarm-tabs { margin: 0 -24px; }
    :host ::ng-deep .alarm-tabs .mat-mdc-tab-body-wrapper { padding: 0 24px; }
    .tab-content { padding: 16px 0; }
    .tab-label { margin-left: 6px; }
    .team-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
    .team-label { display: flex; align-items: center; gap: 8px; }
    .team-dot { width: 12px; height: 12px; border-radius: 50%; display: inline-block; }
    .full-width { width: 100%; } h4 { margin: 16px 0 8px; color: rgba(0,0,0,0.64); }
    mat-slide-toggle { display: block; margin: 16px 0; }
    .selection-count { color: rgba(0,0,0,0.54); font-size: 14px; margin-top: 8px; }
    .distance-radio-group { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .radio-label { display: flex; align-items: flex-start; gap: 8px; }
    .radio-label mat-icon { margin-top: 2px; color: rgba(0,0,0,0.54); }
    .radio-hint { margin: 2px 0 0; font-size: 12px; color: rgba(0,0,0,0.54); font-weight: normal; }
  `],
})
export class GymAddDialogComponent {
  readonly dialogRef = inject(MatDialogRef<GymAddDialogComponent>);
  private readonly gymService = inject(GymService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  saving = signal(false); selectedTeamIds = signal<number[]>([]);
  teams: TeamOption[] = [
    { id: 0, name: 'Neutral', color: '#9E9E9E' }, { id: 1, name: 'Mystic (Blue)', color: '#2196F3' },
    { id: 2, name: 'Valor (Red)', color: '#F44336' }, { id: 3, name: 'Instinct (Yellow)', color: '#FFC107' },
  ];
  form = this.fb.group({ slot_changes: [false], battle_changes: [false], distanceMode: ['areas' as 'areas' | 'distance'], distanceKm: [1], ping: [''], template: [''], clean: [false] });
  toggleTeam(id: number): void { this.selectedTeamIds.update(ids => ids.includes(id) ? ids.filter(i => i !== id) : [...ids, id]); }
  onDistanceModeChange(): void { if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0); else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1); }
  save(): void {
    if (this.selectedTeamIds().length === 0) return;
    this.saving.set(true); const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = this.selectedTeamIds().map(team => this.gymService.create({ team, slot_changes: v.slot_changes ? 1 : 0, battle_changes: v.battle_changes ? 1 : 0, distance: dist, clean: v.clean ? 1 : 0, template: v.template || null, ping: v.ping || null, profileNo: 1, gymId: null }));
    forkJoin(creates).subscribe({
      next: () => { this.snackBar.open(`${creates.length} gym alarm(s) created`, 'OK', { duration: 3000 }); this.dialogRef.close(true); },
      error: () => { this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 }); this.saving.set(false); },
    });
  }
}
