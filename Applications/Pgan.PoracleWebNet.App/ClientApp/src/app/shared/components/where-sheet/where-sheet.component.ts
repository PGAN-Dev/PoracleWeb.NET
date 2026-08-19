import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';

import { AreaService } from '../../../core/services/area.service';
import { PlacesService } from '../../../core/services/places.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { AlarmScope, AlarmScopeMode, titleCaseArea } from '../../utils/alarm-scope';

export interface WhereSheetData {
  /** Areas the profile subscribes to, so the inherited option can say what it means. */
  profileAreas: string[];
  scope: AlarmScope;
}

/**
 * The one place an alarm's delivery scope is chosen, shared by every alarm dialog and every card.
 *
 * The three options are a radio group rather than three independent fields because PoracleNG treats
 * them as mutually exclusive: a place with areas, areas with a radius, or a place without one are all
 * refused. Modelled as a choice, those states cannot be expressed, so there is nothing to validate and
 * no error copy to write.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    TranslatePipe,
  ],
  selector: 'app-where-sheet',
  standalone: true,
  styleUrl: './where-sheet.component.scss',
  templateUrl: './where-sheet.component.html',
})
export class WhereSheetComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly geofenceService = inject(UserGeofenceService);
  /**
   * Admin areas plus the user's own geofences. Their own are listed because PoracleWeb writes them
   * past PoracleNG's user-selectable filter; without that they would be offered and then refused.
   */
  readonly availableAreas = signal<{ name: string; own: boolean }[]>([]);
  readonly data = inject<WhereSheetData>(MAT_DIALOG_DATA);
  readonly distanceKm = signal<number>(this.data.scope.distanceKm ?? 1);

  readonly mode = signal<AlarmScopeMode>(this.data.scope.mode);
  readonly placeLabel = signal<string>(this.data.scope.placeLabel ?? '');
  readonly selectedAreas = signal<string[]>(this.data.scope.areas ?? []);
  readonly canSave = computed(() => {
    switch (this.mode()) {
      case 'areas':
        return this.selectedAreas().length > 0;
      case 'place':
        return this.placeLabel().length > 0 && this.distanceKm() > 0;
      default:
        return true;
    }
  });

  readonly dialogRef = inject<MatDialogRef<WhereSheetComponent, AlarmScope>>(MatDialogRef);

  readonly places = inject(PlacesService);

  readonly profileAreaSummary = computed(() => this.data.profileAreas.map(titleCaseArea).join(', '));

  ngOnInit(): void {
    this.places.load().subscribe({ error: () => undefined });

    this.areaService.getAvailable().subscribe({
      error: () => undefined,
      next: areas => this.availableAreas.update(current => [...areas.map(a => ({ name: a.name, own: false })), ...current]),
    });

    this.geofenceService.getCustomGeofences().subscribe({
      error: () => undefined,
      next: own => this.availableAreas.update(current => [...current, ...own.map(g => ({ name: g.kojiName, own: true }))]),
    });
  }

  save(): void {
    this.dialogRef.close(this.currentScope());
  }

  protected titleCase(area: string): string {
    return titleCaseArea(area);
  }

  private currentScope(): AlarmScope {
    switch (this.mode()) {
      case 'areas':
        return { areas: this.selectedAreas(), mode: 'areas' };
      case 'place':
        return { distanceKm: this.distanceKm(), mode: 'place', placeLabel: this.placeLabel() };
      default:
        return { mode: 'profile' };
    }
  }
}
