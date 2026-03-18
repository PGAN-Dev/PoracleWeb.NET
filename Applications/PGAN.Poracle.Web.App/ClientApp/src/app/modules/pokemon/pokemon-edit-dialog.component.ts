import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MatDialogModule,
  MatDialogRef,
  MAT_DIALOG_DATA,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MonsterService } from '../../core/services/monster.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { IconService } from '../../core/services/icon.service';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { Monster, MonsterUpdate } from '../../core/models';

@Component({
  selector: 'app-pokemon-edit-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconModule,
    MatExpansionModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  template: `
    <h2 mat-dialog-title>
      Edit {{ pokemonName }} Alarm
    </h2>
    <mat-dialog-content>
      <div class="pokemon-header">
        <img
          [src]="getPokemonImage()"
          [alt]="pokemonName"
          class="pokemon-img"
          (error)="onImageError($event)"
        />
        <div>
          <h3>{{ pokemonName }}</h3>
          <span class="pokemon-id">#{{ data.pokemonId }}</span>
        </div>
      </div>

      <mat-tab-group animationDuration="200ms" class="alarm-tabs">
        <!-- Tab 1: Filters -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>tune</mat-icon>
            <span class="tab-label">Filters</span>
          </ng-template>
          <div class="tab-content">
            <h4>IV / CP / Level</h4>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min IV</mat-label>
                <input matInput type="number" [formControl]="form.controls.minIv" min="0" max="100" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max IV</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxIv" min="0" max="100" />
              </mat-form-field>
            </div>

            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min CP</mat-label>
                <input matInput type="number" [formControl]="form.controls.minCp" min="0" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max CP</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxCp" min="0" />
              </mat-form-field>
            </div>

            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min Level</mat-label>
                <input matInput type="number" [formControl]="form.controls.minLevel" min="0" max="50" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max Level</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxLevel" min="0" max="50" />
              </mat-form-field>
            </div>

            <h4>Stats</h4>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min ATK</mat-label>
                <input matInput type="number" [formControl]="form.controls.atk" min="0" max="15" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max ATK</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxAtk" min="0" max="15" />
              </mat-form-field>
            </div>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min DEF</mat-label>
                <input matInput type="number" [formControl]="form.controls.def" min="0" max="15" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max DEF</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxDef" min="0" max="15" />
              </mat-form-field>
            </div>
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Min STA</mat-label>
                <input matInput type="number" [formControl]="form.controls.sta" min="0" max="15" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Max STA</mat-label>
                <input matInput type="number" [formControl]="form.controls.maxSta" min="0" max="15" />
              </mat-form-field>
            </div>

            @if (availableForms().length > 0) {
              <h4>Form & Gender</h4>
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Form</mat-label>
                  <mat-select [formControl]="form.controls.form">
                    <mat-option [value]="0">All Forms</mat-option>
                    @for (f of availableForms(); track f.id) {
                      <mat-option [value]="f.id">{{ f.name }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Gender</mat-label>
                  <mat-select [formControl]="form.controls.gender">
                    <mat-option [value]="0">All</mat-option>
                    <mat-option [value]="1">♂ Male</mat-option>
                    <mat-option [value]="2">♀ Female</mat-option>
                    <mat-option [value]="3">Genderless</mat-option>
                  </mat-select>
                </mat-form-field>
              </div>
            }

            <mat-expansion-panel>
              <mat-expansion-panel-header>
                <mat-panel-title>
                  <mat-icon>more_horiz</mat-icon> More Filters
                </mat-panel-title>
              </mat-expansion-panel-header>
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Min Weight</mat-label>
                  <input matInput type="number" [formControl]="form.controls.minWeight" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Max Weight</mat-label>
                  <input matInput type="number" [formControl]="form.controls.maxWeight" />
                </mat-form-field>
              </div>
              @if (availableForms().length === 0) {
                <div class="form-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Gender</mat-label>
                    <mat-select [formControl]="form.controls.gender">
                      <mat-option [value]="0">All</mat-option>
                      <mat-option [value]="1">♂ Male</mat-option>
                      <mat-option [value]="2">♀ Female</mat-option>
                      <mat-option [value]="3">Genderless</mat-option>
                    </mat-select>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Form ID</mat-label>
                    <input matInput type="number" [formControl]="form.controls.form" />
                    <mat-hint>0 = all forms</mat-hint>
                  </mat-form-field>
                </div>
              }
            </mat-expansion-panel>
          </div>
        </mat-tab>

        <!-- Tab 2: PVP -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>emoji_events</mat-icon>
            <span class="tab-label">PVP</span>
          </ng-template>
          <div class="tab-content">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>PVP League</mat-label>
              <mat-select [formControl]="form.controls.pvpRankingLeague">
                <mat-option [value]="0">None</mat-option>
                <mat-option [value]="500">Little Cup (500)</mat-option>
                <mat-option [value]="1500">Great League (1500)</mat-option>
                <mat-option [value]="2500">Ultra League (2500)</mat-option>
              </mat-select>
            </mat-form-field>

            @if (form.controls.pvpRankingLeague.value !== 0) {
              <div class="form-row">
                <mat-form-field appearance="outline">
                  <mat-label>Best Rank</mat-label>
                  <input matInput type="number" [formControl]="form.controls.pvpRankingBest" min="1" max="4096" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Worst Rank</mat-label>
                  <input matInput type="number" [formControl]="form.controls.pvpRankingWorst" min="1" max="4096" />
                </mat-form-field>
              </div>
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Min CP for League</mat-label>
                <input matInput type="number" [formControl]="form.controls.pvpRankingMinCp" min="0" />
              </mat-form-field>
            }
          </div>
        </mat-tab>

        <!-- Tab 3: Delivery -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>notifications</mat-icon>
            <span class="tab-label">Delivery</span>
          </ng-template>
          <div class="tab-content">
            <h4>Location Mode</h4>
            <mat-radio-group
              [formControl]="form.controls.distanceMode"
              class="distance-radio-group"
              (change)="onDistanceModeChange()"
            >
              <mat-radio-button value="areas">
                <div class="radio-label">
                  <mat-icon>map</mat-icon>
                  <div>
                    <strong>Use Areas</strong>
                    <p class="radio-hint">Notifications will use your configured area geofences</p>
                  </div>
                </div>
              </mat-radio-button>
              <mat-radio-button value="distance">
                <div class="radio-label">
                  <mat-icon>straighten</mat-icon>
                  <div>
                    <strong>Set Distance</strong>
                    <p class="radio-hint">Notify within a radius from your location</p>
                  </div>
                </div>
              </mat-radio-button>
            </mat-radio-group>

            @if (form.controls.distanceMode.value === 'distance') {
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Distance</mat-label>
                <input matInput type="number" [formControl]="form.controls.distanceKm" min="0" step="0.1" />
                <span matSuffix>km</span>
              </mat-form-field>
            }

            <app-delivery-preview
              [mode]="form.controls.distanceMode.value === 'areas' ? 'areas' : 'distance'"
              [distanceKm]="form.controls.distanceKm.value ?? 0">
            </app-delivery-preview>

            <h4>Notifications</h4>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Ping / Role</mat-label>
              <input matInput [formControl]="form.controls.ping" />
            </mat-form-field>

            <app-template-selector
              [alarmType]="'monster'"
              [value]="form.controls.template.value ?? ''"
              (valueChange)="form.controls.template.setValue($event)">
            </app-template-selector>

            <mat-slide-toggle [formControl]="form.controls.clean">
              Clean mode
            </mat-slide-toggle>
          </div>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancel</button>
      <button
        mat-raised-button
        color="primary"
        (click)="save()"
        [disabled]="saving()"
      >
        {{ saving() ? 'Saving...' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      mat-dialog-content {
        min-width: 400px;
        max-width: 600px;
      }
      .alarm-tabs { margin: 0 -24px; }
      :host ::ng-deep .alarm-tabs .mat-mdc-tab-body-wrapper { padding: 0 24px; }
      .tab-content { padding: 16px 0; }
      .tab-label { margin-left: 6px; }
      .pokemon-header {
        display: flex;
        align-items: center;
        gap: 16px;
        margin-bottom: 16px;
      }
      .pokemon-img {
        width: 64px;
        height: 64px;
        object-fit: contain;
      }
      .pokemon-id {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .form-row {
        display: flex;
        gap: 16px;
      }
      .form-row mat-form-field {
        flex: 1;
      }
      .full-width {
        width: 100%;
      }
      h4 {
        margin: 16px 0 8px;
        color: var(--text-muted, rgba(0, 0, 0, 0.64));
      }
      mat-slide-toggle {
        margin: 16px 0;
      }
      mat-expansion-panel {
        margin: 16px 0;
      }
      .distance-radio-group {
        display: flex;
        flex-direction: column;
        gap: 12px;
        margin-bottom: 16px;
      }
      .radio-label {
        display: flex;
        align-items: flex-start;
        gap: 8px;
      }
      .radio-label mat-icon {
        margin-top: 2px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .radio-hint {
        margin: 2px 0 0;
        font-size: 12px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        font-weight: normal;
      }
    `,
  ],
})
export class PokemonEditDialogComponent {
  readonly dialogRef = inject(MatDialogRef<PokemonEditDialogComponent>);
  readonly data = inject<Monster>(MAT_DIALOG_DATA);
  private readonly monsterService = inject(MonsterService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  private readonly iconService = inject(IconService);

  saving = signal(false);

  readonly availableForms = computed(() => {
    return this.masterData.getFormsForPokemon(this.data.pokemonId);
  });

  pokemonName = this.data.pokemonId === 0 ? 'All Pokemon' : this.masterData.getPokemonName(this.data.pokemonId);

  form = this.fb.group({
    minIv: [this.data.minIv],
    maxIv: [this.data.maxIv],
    minCp: [this.data.minCp],
    maxCp: [this.data.maxCp],
    minLevel: [this.data.minLevel],
    maxLevel: [this.data.maxLevel],
    atk: [this.data.atk],
    def: [this.data.def],
    sta: [this.data.sta],
    maxAtk: [this.data.maxAtk],
    maxDef: [this.data.maxDef],
    maxSta: [this.data.maxSta],
    minWeight: [this.data.minWeight],
    maxWeight: [this.data.maxWeight],
    gender: [this.data.gender],
    form: [this.data.form],
    pvpRankingLeague: [this.data.pvpRankingLeague],
    pvpRankingBest: [this.data.pvpRankingBest],
    pvpRankingWorst: [this.data.pvpRankingWorst],
    pvpRankingMinCp: [this.data.pvpRankingMinCp],
    distanceMode: [this.data.distance === 0 ? 'areas' : 'distance' as 'areas' | 'distance'],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
    clean: [this.data.clean === 1],
  });

  getPokemonImage(): string {
    return this.iconService.getPokemonUrl(this.data.pokemonId, this.data.form);
  }

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    const fallback = this.iconService.getPokemonFallbackUrl(this.data.pokemonId);
    if (!img.src.endsWith(`/${this.data.pokemonId}.png`)) {
      img.src = fallback;
    } else {
      img.style.display = 'none';
    }
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') {
      this.form.controls.distanceKm.setValue(0);
    } else {
      if (!this.form.controls.distanceKm.value) {
        this.form.controls.distanceKm.setValue(1);
      }
    }
  }

  save(): void {
    this.saving.set(true);
    const values = this.form.getRawValue();

    const distanceMeters = values.distanceMode === 'areas' ? 0 : Math.round((values.distanceKm ?? 1) * 1000);

    const update: MonsterUpdate = {
      minIv: values.minIv ?? 0,
      maxIv: values.maxIv ?? 100,
      minCp: values.minCp ?? 0,
      maxCp: values.maxCp ?? 9000,
      minLevel: values.minLevel ?? 0,
      maxLevel: values.maxLevel ?? 40,
      atk: values.atk ?? 0,
      def: values.def ?? 0,
      sta: values.sta ?? 0,
      maxAtk: values.maxAtk ?? 15,
      maxDef: values.maxDef ?? 15,
      maxSta: values.maxSta ?? 15,
      minWeight: values.minWeight ?? 0,
      maxWeight: values.maxWeight ?? 9000000,
      gender: values.gender ?? 0,
      form: values.form ?? 0,
      pvpRankingLeague: values.pvpRankingLeague ?? 0,
      pvpRankingBest: values.pvpRankingBest ?? 1,
      pvpRankingWorst: values.pvpRankingWorst ?? 100,
      pvpRankingMinCp: values.pvpRankingMinCp ?? 0,
      distance: distanceMeters,
      ping: values.ping || null,
      template: values.template || null,
      clean: values.clean ? 1 : 0,
    };

    this.monsterService.update(this.data.uid, update).subscribe({
      next: () => {
        this.snackBar.open('Pokemon alarm updated', 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
      error: () => {
        this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
    });
  }
}
