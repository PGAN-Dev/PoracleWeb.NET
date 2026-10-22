import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { ReplaySubject } from 'rxjs';

import { GeoJsonImportResult, GeofenceRegion } from '../../../core/models';
import { I18nService } from '../../../core/services/i18n.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { detectRegion } from '../../../shared/utils/geo.utils';
import { TranslatePluralPipe } from '../../pipes/translate-plural.pipe';

export interface GeoJsonImportDialogData {
  currentGeofenceCount: number;
  existingNames: string[];
  maxGeofences: number;
  regions: GeofenceRegion[];
}

interface PreviewFeature {
  editedName: string;
  geometryType: string;
  name: string;
  pointCount: number;
  polygon: [number, number][];
  rawFeature: Record<string, unknown>;
  regionId: number | null;
  selected: boolean;
  warning?: string;
}

type DialogStep = 'upload' | 'preview' | 'results';

/** The most features one import reads; the rest of the file is left out and the preview says how many. */
const MAX_FEATURES = 50;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTooltipModule,
    TranslatePipe,
    TranslatePluralPipe,
  ],
  selector: 'app-geojson-import-dialog',
  standalone: true,
  styleUrl: './geojson-import-dialog.component.scss',
  templateUrl: './geojson-import-dialog.component.html',
})
export class GeoJsonImportDialogComponent {
  private readonly i18n = inject(I18nService);
  private readonly userGeofenceService = inject(UserGeofenceService);
  readonly previewFeatures = signal<PreviewFeature[]>([]);
  readonly allSelected = computed(() => {
    const features = this.previewFeatures();
    return features.length > 0 && features.every(f => f.selected);
  });

  readonly data = inject<GeoJsonImportDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GeoJsonImportDialogComponent>);
  readonly dragOver = signal(false);
  readonly fileError = signal<string | null>(null);
  readonly importing = signal(false);
  readonly importResult = signal<GeoJsonImportResult | null>(null);
  /** Fences saved by the import that PoracleNG couldn't switch on yet; the toggle on each retries. */
  readonly notActivated = computed(() => (this.importResult()?.created ?? []).filter(g => g.activationFailed));
  /** Features past the first MAX_FEATURES in the file, which the preview never read. */
  readonly overLimitCount = signal(0);
  readonly parseError = signal<string | null>(null);
  readonly selectedFeatures = computed(() => this.previewFeatures().filter(f => f.selected));

  readonly selectedFile = signal<File | null>(null);
  readonly selectedValidCount = computed(
    () => this.selectedFeatures().filter(f => !f.warning && !this.getNameError(f) && f.regionId !== null).length,
  );

  /**
   * The import request's outcome (null when it failed). The request isn't tied to the dialog's life: the
   * fences are created whether or not the dialog is still open, so the page can refresh when it settles.
   */
  readonly settled = new ReplaySubject<GeoJsonImportResult | null>(1);

  /** Features in the file that aren't polygons (points, lines), which can't become a geofence. */
  readonly skippedCount = signal(0);

  readonly step = signal<DialogStep>('upload');

  get dialogTitle(): string {
    switch (this.step()) {
      case 'upload':
        return this.i18n.instant('GEOJSON_IMPORT.TITLE_UPLOAD');
      case 'preview':
        return this.i18n.instant('GEOJSON_IMPORT.TITLE_PREVIEW');
      case 'results':
        return this.i18n.instant('GEOJSON_IMPORT.TITLE_RESULTS');
    }
  }

  get hasErrors(): boolean {
    return this.selectedFeatures().some(f => !!this.getNameError(f) || f.regionId === null);
  }

  get remainingSlots(): number {
    return this.data.maxGeofences - this.data.currentGeofenceCount;
  }

  get wouldExceedLimit(): boolean {
    return this.selectedValidCount() > this.remainingSlots;
  }

  closeWithResult(): void {
    this.dialogRef.close(this.importResult());
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  getNameError(feature: PreviewFeature): string | null {
    if (!feature.selected) return null;
    const name = feature.editedName.trim();
    if (!name) return this.i18n.instant('GEOJSON_IMPORT.ERR_NAME_REQUIRED');
    if (name.length > 50) return this.i18n.instant('GEOJSON_IMPORT.ERR_NAME_MAX_LENGTH');
    if (!/^[a-zA-Z0-9 \-'.()&]+$/.test(name)) return this.i18n.instant('GEOJSON_IMPORT.ERR_NAME_INVALID_CHARS');
    const lowerName = name.toLowerCase();
    if (this.data.existingNames.some(n => n.toLowerCase() === lowerName)) return this.i18n.instant('GEOJSON_IMPORT.ERR_NAME_EXISTS');
    const others = this.previewFeatures().filter(f => f !== feature && f.selected);
    if (others.some(f => f.editedName.trim().toLowerCase() === lowerName)) return this.i18n.instant('GEOJSON_IMPORT.ERR_NAME_DUPLICATE');
    return null;
  }

  goBack(): void {
    this.step.set('upload');
    this.selectedFile.set(null);
    this.previewFeatures.set([]);
    this.skippedCount.set(0);
    this.overLimitCount.set(0);
    this.parseError.set(null);
    this.fileError.set(null);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(false);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) {
      this.processFile(file);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.processFile(file);
    }
    input.value = '';
  }

  startImport(): void {
    const selected = this.selectedFeatures().filter(f => !f.warning && !this.getNameError(f) && f.regionId !== null);
    if (selected.length === 0) return;

    // Build a new GeoJSON FeatureCollection with selected features, edited names, and region info
    const features = selected.map(f => {
      const raw = { ...f.rawFeature };
      const props = { ...((raw['properties'] as Record<string, unknown>) ?? {}) };
      props['name'] = f.editedName.trim();
      const region = this.data.regions.find(r => r.id === f.regionId);
      if (region) {
        props['group'] = region.displayName;
        props['parentId'] = region.id;
      }
      raw['properties'] = props;
      return raw;
    });
    const geoJson = JSON.stringify({ features, type: 'FeatureCollection' });
    const blob = new Blob([geoJson], { type: 'application/geo+json' });
    const file = new File([blob], 'import.geojson', { type: 'application/geo+json' });

    this.importing.set(true);
    this.userGeofenceService.importGeoJson(file).subscribe({
      error: err => {
        this.importing.set(false);
        this.parseError.set(err?.error?.error ?? this.i18n.instant('GEOJSON_IMPORT.ERR_IMPORT_FAILED'));
        this.settled.next(null);
      },
      next: result => {
        this.importing.set(false);
        this.importResult.set(result);
        this.step.set('results');
        this.settled.next(result);
      },
    });
  }

  toggleAll(checked: boolean): void {
    this.previewFeatures.update(features => features.map(f => ({ ...f, selected: checked })));
  }

  toggleFeature(index: number): void {
    this.previewFeatures.update(features => features.map((f, i) => (i === index ? { ...f, selected: !f.selected } : f)));
  }

  updateName(index: number, name: string): void {
    this.previewFeatures.update(features => features.map((f, i) => (i === index ? { ...f, editedName: name } : f)));
  }

  updateRegion(index: number, regionId: number | null): void {
    this.previewFeatures.update(features => features.map((f, i) => (i === index ? { ...f, regionId } : f)));
  }

  /** Builds the preview, or sets the error and answers false so the dialog stays on the upload step. */
  private parseGeoJson(json: Record<string, unknown>): boolean {
    let features: Record<string, unknown>[] = [];

    if (json['type'] === 'FeatureCollection' && Array.isArray(json['features'])) {
      features = json['features'] as Record<string, unknown>[];
    } else if (json['type'] === 'Feature') {
      features = [json];
    } else {
      this.parseError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_INVALID_GEOJSON'));
      this.selectedFile.set(null);
      return false;
    }

    // Build region detection data from regions with polygons
    const regionDetectionData = this.data.regions
      .filter(r => r.polygon && r.polygon.length > 0)
      .map(r => ({ id: r.id, name: r.name, displayName: r.displayName, path: r.polygon! }));

    const previews: PreviewFeature[] = [];
    let autoNameIndex = 1;
    let skipped = 0;

    for (const feature of features.slice(0, MAX_FEATURES)) {
      const geometry = feature?.['geometry'] as Record<string, unknown> | undefined;
      const geomType = geometry?.['type'] as string | undefined;
      if (!geometry || (geomType !== 'Polygon' && geomType !== 'MultiPolygon')) {
        skipped++;
        continue;
      }

      const props = (feature['properties'] as Record<string, unknown>) ?? {};
      const name =
        (props['name'] as string) ??
        (props['__name'] as string) ??
        this.i18n.instant('GEOJSON_IMPORT.AUTO_NAME', { number: autoNameIndex++ });

      let pointCount = 0;
      let polygon: [number, number][] = [];
      try {
        const coords = geometry['coordinates'] as unknown[][][];
        let ring: unknown[][] = [];
        if (geomType === 'Polygon') {
          ring = coords[0] as unknown[][];
        } else if (geomType === 'MultiPolygon') {
          ring = (coords as unknown[][][][])[0][0] as unknown[][];
        }
        pointCount = ring.length;
        if (pointCount > 0) pointCount--;
        // Convert GeoJSON [lon, lat] to internal [lat, lon] for region detection
        polygon = ring.map(c => [(c as number[])[1], (c as number[])[0]] as [number, number]);
        // Remove closing vertex
        if (polygon.length >= 2) {
          const first = polygon[0];
          const last = polygon[polygon.length - 1];
          if (Math.abs(first[0] - last[0]) < 1e-9 && Math.abs(first[1] - last[1]) < 1e-9) {
            polygon = polygon.slice(0, -1);
          }
        }
      } catch {
        pointCount = 0;
      }

      let warning: string | undefined;
      if (pointCount < 3) {
        warning = this.i18n.instant('GEOJSON_IMPORT.ERR_TOO_FEW_POINTS');
      } else if (pointCount > 500) {
        warning = this.i18n.instant('GEOJSON_IMPORT.ERR_TOO_MANY_POINTS');
      }

      // Auto-detect region from polygon centroid
      let regionId: number | null = null;
      if (polygon.length >= 3 && regionDetectionData.length > 0) {
        const detected = detectRegion(polygon, regionDetectionData);
        if (detected) {
          regionId = detected.id;
        }
      }

      previews.push({
        name,
        editedName: name,
        geometryType: geomType,
        pointCount,
        polygon,
        rawFeature: feature,
        regionId,
        selected: true,
        warning,
      });
    }

    if (previews.length === 0) {
      this.parseError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_NO_POLYGONS'));
      this.selectedFile.set(null);
      return false;
    }

    this.previewFeatures.set(previews);
    this.skippedCount.set(skipped);
    this.overLimitCount.set(Math.max(0, features.length - MAX_FEATURES));
    return true;
  }

  private processFile(file: File): void {
    this.fileError.set(null);
    this.parseError.set(null);

    const validTypes = ['.geojson', '.json'];
    const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
    if (!validTypes.includes(ext)) {
      this.fileError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_INVALID_FILE_TYPE'));
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.fileError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_FILE_TOO_LARGE'));
      return;
    }

    this.selectedFile.set(file);

    const reader = new FileReader();
    reader.onload = () => {
      try {
        const json = JSON.parse(reader.result as string);
        // A file that parses but isn't usable GeoJSON used to move on to the preview step anyway, showing
        // "0 B" with the error underneath.
        if (this.parseGeoJson(json)) this.step.set('preview');
      } catch {
        this.parseError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_INVALID_JSON'));
        this.selectedFile.set(null);
      }
    };
    reader.onerror = () => {
      this.parseError.set(this.i18n.instant('GEOJSON_IMPORT.ERR_READ_FAILED'));
      this.selectedFile.set(null);
    };
    reader.readAsText(file);
  }
}
