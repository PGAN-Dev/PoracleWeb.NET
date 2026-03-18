import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { QuestService } from '../../core/services/quest.service';
import { QuestCreate } from '../../core/models';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'app-quest-add-dialog',
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
    MatTabsModule,
    MatRadioModule,
    MatSnackBarModule,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  template: `
    <h2 mat-dialog-title>Add Quest Alarm</h2>
    <mat-dialog-content>
      <mat-tab-group animationDuration="200ms" class="alarm-tabs">
        <!-- Tab 1: Rewards -->
        <mat-tab>
          <ng-template mat-tab-label>
            <mat-icon>explore</mat-icon>
            <span class="tab-label">Rewards</span>
          </ng-template>
          <div class="tab-content">
            <mat-tab-group [(selectedIndex)]="tabIndex" class="reward-tabs">
              <mat-tab label="Pokemon">
                <div class="tab-content">
                  <app-pokemon-selector [multi]="true" (selectionChange)="onPokemonSelected($event)" />
                  @if (selectedPokemonIds().length > 0) {
                    <p class="selection-count">{{ selectedPokemonIds().length }} Pokemon selected</p>
                  }
                </div>
              </mat-tab>
              <mat-tab label="Items">
                <div class="tab-content">
                  <mat-form-field appearance="outline" class="full-width">
                    <mat-label>Item Reward</mat-label>
                    <mat-select [formControl]="itemForm.controls.reward">
                      <mat-option [value]="0">Any Item</mat-option>
                      <mat-option [value]="1">Poke Ball</mat-option>
                      <mat-option [value]="2">Great Ball</mat-option>
                      <mat-option [value]="3">Ultra Ball</mat-option>
                      <mat-option [value]="101">Potion</mat-option>
                      <mat-option [value]="102">Super Potion</mat-option>
                      <mat-option [value]="103">Hyper Potion</mat-option>
                      <mat-option [value]="104">Max Potion</mat-option>
                      <mat-option [value]="201">Revive</mat-option>
                      <mat-option [value]="202">Max Revive</mat-option>
                      <mat-option [value]="301">Rare Candy</mat-option>
                      <mat-option [value]="401">Silver Pinap Berry</mat-option>
                      <mat-option [value]="501">Star Piece</mat-option>
                      <mat-option [value]="502">Lucky Egg</mat-option>
                      <mat-option [value]="503">Incense</mat-option>
                      <mat-option [value]="504">Lure Module</mat-option>
                      <mat-option [value]="705">Rocket Radar</mat-option>
                      <mat-option [value]="1301">Stardust</mat-option>
                      <mat-option [value]="1402">XL Candy</mat-option>
                    </mat-select>
                  </mat-form-field>
                </div>
              </mat-tab>
              <mat-tab label="Mega Energy">
                <div class="tab-content">
                  <app-pokemon-selector [multi]="true" (selectionChange)="onMegaPokemonSelected($event)" />
                  @if (selectedMegaPokemonIds().length > 0) {
                    <p class="selection-count">{{ selectedMegaPokemonIds().length }} Pokemon selected</p>
                  }
                </div>
              </mat-tab>
              <mat-tab label="Candy">
                <div class="tab-content">
                  <app-pokemon-selector [multi]="true" (selectionChange)="onCandyPokemonSelected($event)" />
                  @if (selectedCandyPokemonIds().length > 0) {
                    <p class="selection-count">{{ selectedCandyPokemonIds().length }} Pokemon selected</p>
                  }
                </div>
              </mat-tab>
            </mat-tab-group>
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
            <mat-radio-group
              [formControl]="commonForm.controls.distanceMode"
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

            @if (commonForm.controls.distanceMode.value === 'distance') {
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Distance</mat-label>
                <input matInput type="number" [formControl]="commonForm.controls.distanceKm" min="0" step="0.1" />
                <span matSuffix>km</span>
              </mat-form-field>
            }

            <app-delivery-preview
              [mode]="commonForm.controls.distanceMode.value === 'areas' ? 'areas' : 'distance'"
              [distanceKm]="commonForm.controls.distanceKm.value ?? 0">
            </app-delivery-preview>

            <h4>Common Settings</h4>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Ping / Role</mat-label>
              <input matInput [formControl]="commonForm.controls.ping" placeholder="e.g. @role or empty" />
            </mat-form-field>

            <app-template-selector
              [alarmType]="'quest'"
              [value]="commonForm.controls.template.value ?? ''"
              (valueChange)="commonForm.controls.template.setValue($event)">
            </app-template-selector>

            <mat-slide-toggle [formControl]="commonForm.controls.clean">
              Clean mode (auto-delete after quest resets)
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
        [disabled]="saving() || !canSave()"
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
      :host ::ng-deep .reward-tabs .mat-mdc-tab-labels {
        overflow-x: auto;
        -webkit-overflow-scrolling: touch;
        scrollbar-width: none;
      }
      :host ::ng-deep .reward-tabs .mat-mdc-tab-labels::-webkit-scrollbar {
        display: none;
      }
      :host ::ng-deep .reward-tabs .mat-mdc-tab-label-container {
        overflow: visible;
      }
      @media (max-width: 599px) {
        mat-dialog-content {
          min-width: unset;
        }
      }
      .tab-content {
        padding: 16px 0;
      }
      .tab-label { margin-left: 6px; }
      .full-width {
        width: 100%;
      }
      h4 {
        margin: 16px 0 8px;
        color: rgba(0, 0, 0, 0.64);
      }
      mat-slide-toggle {
        margin: 16px 0;
      }
      .selection-count {
        color: rgba(0, 0, 0, 0.54);
        font-size: 14px;
        margin-top: 8px;
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
        color: rgba(0, 0, 0, 0.54);
      }
      .radio-hint {
        margin: 2px 0 0;
        font-size: 12px;
        color: rgba(0, 0, 0, 0.54);
        font-weight: normal;
      }
    `,
  ],
})
export class QuestAddDialogComponent {
  readonly dialogRef = inject(MatDialogRef<QuestAddDialogComponent>);
  private readonly questService = inject(QuestService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);

  tabIndex = 0;
  saving = signal(false);
  selectedPokemonIds = signal<number[]>([]);
  selectedMegaPokemonIds = signal<number[]>([]);
  selectedCandyPokemonIds = signal<number[]>([]);

  itemForm = this.fb.group({
    reward: [0],
  });

  commonForm = this.fb.group({
    distanceMode: ['areas' as 'areas' | 'distance'],
    distanceKm: [1],
    ping: [''],
    template: [''],
    clean: [false],
  });

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  onMegaPokemonSelected(ids: number[]): void {
    this.selectedMegaPokemonIds.set(ids);
  }

  onCandyPokemonSelected(ids: number[]): void {
    this.selectedCandyPokemonIds.set(ids);
  }

  onDistanceModeChange(): void {
    if (this.commonForm.controls.distanceMode.value === 'areas') {
      this.commonForm.controls.distanceKm.setValue(0);
    } else {
      if (!this.commonForm.controls.distanceKm.value) {
        this.commonForm.controls.distanceKm.setValue(1);
      }
    }
  }

  canSave(): boolean {
    switch (this.tabIndex) {
      case 0: return this.selectedPokemonIds().length > 0;
      case 1: return true;
      case 2: return this.selectedMegaPokemonIds().length > 0;
      case 3: return this.selectedCandyPokemonIds().length > 0;
      default: return false;
    }
  }

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    const common = this.commonForm.getRawValue();
    const distanceMeters = common.distanceMode === 'areas' ? 0 : Math.round((common.distanceKm ?? 1) * 1000);

    const creates: ReturnType<typeof this.questService.create>[] = [];

    switch (this.tabIndex) {
      case 0:
        for (const pokemonId of this.selectedPokemonIds()) {
          creates.push(this.questService.create({
            pokemonId, rewardType: 7, reward: pokemonId, shiny: 0,
            distance: distanceMeters, clean: common.clean ? 1 : 0,
            template: common.template || null, ping: common.ping || null, profileNo: 1,
          }));
        }
        break;
      case 1:
        creates.push(this.questService.create({
          pokemonId: 0, rewardType: 2, reward: this.itemForm.controls.reward.value ?? 0, shiny: 0,
          distance: distanceMeters, clean: common.clean ? 1 : 0,
          template: common.template || null, ping: common.ping || null, profileNo: 1,
        }));
        break;
      case 2:
        for (const pokemonId of this.selectedMegaPokemonIds()) {
          creates.push(this.questService.create({
            pokemonId, rewardType: 12, reward: pokemonId, shiny: 0,
            distance: distanceMeters, clean: common.clean ? 1 : 0,
            template: common.template || null, ping: common.ping || null, profileNo: 1,
          }));
        }
        break;
      case 3:
        for (const pokemonId of this.selectedCandyPokemonIds()) {
          creates.push(this.questService.create({
            pokemonId, rewardType: 4, reward: pokemonId, shiny: 0,
            distance: distanceMeters, clean: common.clean ? 1 : 0,
            template: common.template || null, ping: common.ping || null, profileNo: 1,
          }));
        }
        break;
    }

    forkJoin(creates).subscribe({
      next: () => {
        this.snackBar.open(`${creates.length} quest alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
    });
  }
}
