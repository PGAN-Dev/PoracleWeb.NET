import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { MonsterService } from '../../core/services/monster.service';
import { MonsterCreate } from '../../core/models';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'app-pokemon-add-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatSlideToggleModule, MatIconModule,
    MatTabsModule, MatExpansionModule, MatRadioModule, MatSnackBarModule,
    MatProgressSpinnerModule, PokemonSelectorComponent, TemplateSelectorComponent, DeliveryPreviewComponent,
  ],
  template: `
    <h2 mat-dialog-title>Add Pokemon Alarm</h2>
    <mat-dialog-content>
      <mat-tab-group animationDuration="200ms" class="alarm-tabs">
        <!-- Tab 1: Pokemon -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>catching_pokemon</mat-icon>
            <span class="tab-label">Pokemon</span>
          </ng-template>
          <div class="tab-content">
            <app-pokemon-selector [multi]="true" (selectionChange)="onPokemonSelected($event)" />
            @if (selectedPokemonIds().length > 0) {
              <p class="selection-count">{{ selectedPokemonIds().length }} Pokemon selected</p>
            } @else {
              <p class="selection-hint">Search and select one or more Pokemon to track</p>
            }
          </div>
        </mat-tab>

        <!-- Tab 2: Filters -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>tune</mat-icon>
            <span class="tab-label">Filters</span>
          </ng-template>
          <div class="tab-content">
            <h4>IV Range</h4>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min IV</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.minIv" />
                <span matTextSuffix>%</span>
                @if (filtersForm.controls.minIv.invalid) { <mat-error>0-100</mat-error> }
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max IV</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxIv" />
                <span matTextSuffix>%</span>
                @if (filtersForm.controls.maxIv.invalid) { <mat-error>0-100</mat-error> }
              </mat-form-field>
            </div>

            <h4>CP & Level</h4>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min CP</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.minCp" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max CP</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxCp" />
              </mat-form-field>
            </div>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min Level</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.minLevel" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max Level</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxLevel" />
              </mat-form-field>
            </div>

            <h4>Individual Stats (ATK / DEF / STA)</h4>
            <div class="form-row triple">
              <mat-form-field appearance="outline">
                <mat-label>Min ATK</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.atk" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Min DEF</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.def" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Min STA</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.sta" />
              </mat-form-field>
            </div>
            <div class="form-row triple">
              <mat-form-field appearance="outline">
                <mat-label>Max ATK</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxAtk" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max DEF</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxDef" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max STA</mat-label>
                <input matInput type="number" [formControl]="filtersForm.controls.maxSta" />
              </mat-form-field>
            </div>

            <mat-expansion-panel>
              <mat-expansion-panel-header>
                <mat-panel-title>
                  <mat-icon>more_horiz</mat-icon> More Filters
                </mat-panel-title>
              </mat-expansion-panel-header>
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Min Weight</mat-label>
                  <input matInput type="number" [formControl]="filtersForm.controls.minWeight" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Max Weight</mat-label>
                  <input matInput type="number" [formControl]="filtersForm.controls.maxWeight" />
                </mat-form-field>
              </div>
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Gender</mat-label>
                  <mat-select [formControl]="filtersForm.controls.gender">
                    <mat-option [value]="0">All</mat-option>
                    <mat-option [value]="1">♂ Male</mat-option>
                    <mat-option [value]="2">♀ Female</mat-option>
                    <mat-option [value]="3">Genderless</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Form</mat-label>
                  <input matInput type="number" [formControl]="filtersForm.controls.form" />
                  <mat-hint>0 = all forms</mat-hint>
                </mat-form-field>
              </div>
            </mat-expansion-panel>
          </div>
        </mat-tab>

        <!-- Tab 3: PVP -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>emoji_events</mat-icon>
            <span class="tab-label">PVP</span>
          </ng-template>
          <div class="tab-content">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>PVP League</mat-label>
              <mat-select [formControl]="pvpForm.controls.pvpRankingLeague">
                <mat-option [value]="0">None (disabled)</mat-option>
                <mat-option [value]="500">Little Cup (500 CP)</mat-option>
                <mat-option [value]="1500">Great League (1500 CP)</mat-option>
                <mat-option [value]="2500">Ultra League (2500 CP)</mat-option>
              </mat-select>
            </mat-form-field>

            @if (pvpForm.controls.pvpRankingLeague.value !== 0) {
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Best Rank</mat-label>
                  <input matInput type="number" [formControl]="pvpForm.controls.pvpRankingBest" />
                  <mat-hint>e.g. 1</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Worst Rank</mat-label>
                  <input matInput type="number" [formControl]="pvpForm.controls.pvpRankingWorst" />
                  <mat-hint>e.g. 100</mat-hint>
                </mat-form-field>
              </div>
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Min CP for League</mat-label>
                <input matInput type="number" [formControl]="pvpForm.controls.pvpRankingMinCp" />
                <mat-hint>Only alert if evolved CP meets this minimum</mat-hint>
              </mat-form-field>
            } @else {
              <div class="pvp-disabled-hint">
                <mat-icon>info_outline</mat-icon>
                <p>Select a league to filter by PVP rank. This notifies you when a Pokemon's IVs rank highly for PVP battles.</p>
              </div>
            }
          </div>
        </mat-tab>

        <!-- Tab 4: Notification -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>notifications</mat-icon>
            <span class="tab-label">Delivery</span>
          </ng-template>
          <div class="tab-content">
            <h4>Location Mode</h4>
            <mat-radio-group [formControl]="notifForm.controls.distanceMode" class="distance-radio-group" (change)="onDistanceModeChange()">
              <mat-radio-button value="areas">
                <div class="radio-label"><mat-icon>map</mat-icon><div><strong>Use Areas</strong><p class="radio-hint">Notify based on your configured geofence areas</p></div></div>
              </mat-radio-button>
              <mat-radio-button value="distance">
                <div class="radio-label"><mat-icon>straighten</mat-icon><div><strong>Set Distance</strong><p class="radio-hint">Notify within a radius from your location</p></div></div>
              </mat-radio-button>
            </mat-radio-group>

            @if (notifForm.controls.distanceMode.value === 'distance') {
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Distance</mat-label>
                <input matInput type="number" [formControl]="notifForm.controls.distanceKm" min="0" step="0.1" />
                <span matSuffix>km</span>
              </mat-form-field>
            }

            <app-delivery-preview
              [mode]="notifForm.controls.distanceMode.value === 'areas' ? 'areas' : 'distance'"
              [distanceKm]="notifForm.controls.distanceKm.value ?? 0">
            </app-delivery-preview>

            <h4>Message Settings</h4>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Ping / Role</mat-label>
              <input matInput [formControl]="notifForm.controls.ping" placeholder="e.g. @Pokemon or leave empty" />
              <mat-hint>Discord role to mention in the notification</mat-hint>
            </mat-form-field>

            <app-template-selector [alarmType]="'monster'" [value]="notifForm.controls.template.value ?? ''" (valueChange)="notifForm.controls.template.setValue($event)"></app-template-selector>

            <mat-slide-toggle [formControl]="notifForm.controls.clean">
              Clean mode — auto-delete notification after Pokemon despawns
            </mat-slide-toggle>
          </div>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-raised-button color="primary" (click)="save()" [disabled]="saving() || !isFormValid()">
        @if (saving()) { <mat-spinner diameter="18" class="btn-spinner"></mat-spinner> }
        {{ saving() ? 'Saving...' : 'Save Alarm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 450px; max-width: 600px; }
    .alarm-tabs { margin: 0 -24px; }
    :host ::ng-deep .alarm-tabs .mat-mdc-tab-body-wrapper { padding: 0 24px; }
    .tab-content { padding: 16px 0; }
    .tab-label { margin-left: 6px; }
    .form-row { display: flex; gap: 12px; }
    .form-row mat-form-field { flex: 1; }
    .form-row.triple mat-form-field { flex: 1; min-width: 0; }
    .full-width { width: 100%; }
    .selection-count { color: #2e7d32; font-size: 14px; margin-top: 8px; font-weight: 500; }
    .selection-hint { color: var(--text-hint, rgba(0,0,0,0.38)); font-size: 13px; margin-top: 8px; }
    h4 { margin: 0 0 8px; color: var(--text-muted, rgba(0,0,0,0.64)); font-size: 13px; text-transform: uppercase; letter-spacing: 0.5px; }
    mat-slide-toggle { margin: 16px 0; display: block; }
    mat-expansion-panel { margin: 12px 0; }
    .distance-radio-group { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .radio-label { display: flex; align-items: flex-start; gap: 8px; }
    .radio-label mat-icon { margin-top: 2px; color: var(--text-secondary, rgba(0,0,0,0.54)); }
    .radio-hint { margin: 2px 0 0; font-size: 12px; color: var(--text-secondary, rgba(0,0,0,0.54)); font-weight: normal; }
    .pvp-disabled-hint { display: flex; align-items: flex-start; gap: 8px; padding: 16px; background: var(--skeleton-bg, rgba(0,0,0,0.03)); border-radius: 8px; }
    .pvp-disabled-hint mat-icon { color: var(--text-hint, rgba(0,0,0,0.38)); flex-shrink: 0; margin-top: 2px; }
    .pvp-disabled-hint p { margin: 0; font-size: 13px; color: var(--text-secondary, rgba(0,0,0,0.54)); line-height: 1.5; }
    .btn-spinner { display: inline-block; margin-right: 8px; }
  `],
})
export class PokemonAddDialogComponent {
  readonly dialogRef = inject(MatDialogRef<PokemonAddDialogComponent>);
  private readonly monsterService = inject(MonsterService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);

  selectedPokemonIds = signal<number[]>([]);
  saving = signal(false);

  filtersForm = this.fb.group({
    minIv: [0, [Validators.min(0), Validators.max(100)]],
    maxIv: [100, [Validators.min(0), Validators.max(100)]],
    minCp: [0, [Validators.min(0), Validators.max(9000)]],
    maxCp: [9000, [Validators.min(0), Validators.max(9000)]],
    minLevel: [0, [Validators.min(0), Validators.max(50)]],
    maxLevel: [40, [Validators.min(0), Validators.max(50)]],
    atk: [0, [Validators.min(0), Validators.max(15)]],
    def: [0, [Validators.min(0), Validators.max(15)]],
    sta: [0, [Validators.min(0), Validators.max(15)]],
    maxAtk: [15, [Validators.min(0), Validators.max(15)]],
    maxDef: [15, [Validators.min(0), Validators.max(15)]],
    maxSta: [15, [Validators.min(0), Validators.max(15)]],
    minWeight: [0], maxWeight: [9000000],
    gender: [0], form: [0],
  });

  pvpForm = this.fb.group({
    pvpRankingLeague: [0],
    pvpRankingBest: [1],
    pvpRankingWorst: [100],
    pvpRankingMinCp: [0],
  });

  notifForm = this.fb.group({
    distanceMode: ['areas' as 'areas' | 'distance'],
    distanceKm: [1],
    ping: [''],
    template: [''],
    clean: [false],
  });

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  isFormValid(): boolean {
    return this.selectedPokemonIds().length > 0 && this.filtersForm.valid && this.notifForm.valid;
  }

  onDistanceModeChange(): void {
    if (this.notifForm.controls.distanceMode.value === 'areas') {
      this.notifForm.controls.distanceKm.setValue(0);
    } else if (!this.notifForm.controls.distanceKm.value) {
      this.notifForm.controls.distanceKm.setValue(1);
    }
  }

  save(): void {
    if (!this.isFormValid()) return;
    this.saving.set(true);

    const filters = this.filtersForm.getRawValue();
    const pvp = this.pvpForm.getRawValue();
    const notif = this.notifForm.getRawValue();
    const distanceMeters = notif.distanceMode === 'areas' ? 0 : Math.round((notif.distanceKm ?? 1) * 1000);

    const creates = this.selectedPokemonIds().map(pokemonId => {
      const monster: MonsterCreate = {
        pokemonId, ping: notif.ping || null, distance: distanceMeters,
        minIv: filters.minIv ?? 0, maxIv: filters.maxIv ?? 100,
        minCp: filters.minCp ?? 0, maxCp: filters.maxCp ?? 9000,
        minLevel: filters.minLevel ?? 0, maxLevel: filters.maxLevel ?? 40,
        minWeight: filters.minWeight ?? 0, maxWeight: filters.maxWeight ?? 9000000,
        atk: filters.atk ?? 0, def: filters.def ?? 0, sta: filters.sta ?? 0,
        maxAtk: filters.maxAtk ?? 15, maxDef: filters.maxDef ?? 15, maxSta: filters.maxSta ?? 15,
        pvpRankingWorst: pvp.pvpRankingWorst ?? 100, pvpRankingBest: pvp.pvpRankingBest ?? 1,
        pvpRankingMinCp: pvp.pvpRankingMinCp ?? 0, pvpRankingLeague: pvp.pvpRankingLeague ?? 0,
        form: filters.form ?? 0, gender: filters.gender ?? 0,
        clean: notif.clean ? 1 : 0, template: notif.template || null, profileNo: 1,
      };
      return this.monsterService.create(monster);
    });

    forkJoin(creates).subscribe({
      next: () => {
        this.snackBar.open(`${creates.length} Pokemon alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
    });
  }
}
