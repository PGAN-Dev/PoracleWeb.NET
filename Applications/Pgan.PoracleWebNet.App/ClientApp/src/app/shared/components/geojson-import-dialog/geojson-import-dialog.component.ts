import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { GeoJsonImportResult } from '../../../core/models';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';

export interface GeoJsonImportDialogData {
  currentGeofenceCount: number;
  maxGeofences: number;
}

interface PreviewFeature {
  geometryType: string;
  name: string;
  pointCount: number;
  warning?: string;
}

type DialogStep = 'upload' | 'preview' | 'results';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  selector: 'app-geojson-import-dialog',
  standalone: true,
  styleUrl: './geojson-import-dialog.component.scss',
  templateUrl: './geojson-import-dialog.component.html',
})
export class GeoJsonImportDialogComponent {
  private readonly destroyRef = inject(DestroyRef);
  private readonly userGeofenceService = inject(UserGeofenceService);
  readonly data = inject<GeoJsonImportDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<GeoJsonImportDialogComponent>);

  readonly dragOver = signal(false);
  readonly fileError = signal<string | null>(null);
  readonly importing = signal(false);
  readonly importResult = signal<GeoJsonImportResult | null>(null);
  readonly parseError = signal<string | null>(null);
  readonly previewFeatures = signal<PreviewFeature[]>([]);
  readonly selectedFile = signal<File | null>(null);
  readonly step = signal<DialogStep>('upload');

  get dialogTitle(): string {
    switch (this.step()) {
      case 'upload':
        return 'Import Geofences';
      case 'preview':
        return 'Preview Import';
      case 'results':
        return 'Import Results';
    }
  }

  get remainingSlots(): number {
    return this.data.maxGeofences - this.data.currentGeofenceCount;
  }

  get validFeatureCount(): number {
    return this.previewFeatures().filter(f => !f.warning).length;
  }

  get warningFeatureCount(): number {
    return this.previewFeatures().filter(f => !!f.warning).length;
  }

  get wouldExceedLimit(): boolean {
    return this.validFeatureCount > this.remainingSlots;
  }

  closeWithResult(): void {
    this.dialogRef.close(this.importResult());
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  goBack(): void {
    this.step.set('upload');
    this.selectedFile.set(null);
    this.previewFeatures.set([]);
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
    const file = this.selectedFile();
    if (!file) return;

    this.importing.set(true);
    this.userGeofenceService
      .importGeoJson(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => {
          this.importing.set(false);
          this.parseError.set(err?.error?.error ?? 'Import failed. Please try again.');
        },
        next: result => {
          this.importing.set(false);
          this.importResult.set(result);
          this.step.set('results');
        },
      });
  }

  private parseGeoJson(json: Record<string, unknown>): void {
    let features: Record<string, unknown>[] = [];

    if (json['type'] === 'FeatureCollection' && Array.isArray(json['features'])) {
      features = json['features'] as Record<string, unknown>[];
    } else if (json['type'] === 'Feature') {
      features = [json];
    } else {
      this.parseError.set('File must be a GeoJSON FeatureCollection or Feature');
      this.selectedFile.set(null);
      return;
    }

    const previews: PreviewFeature[] = [];
    let autoNameIndex = 1;

    for (const feature of features.slice(0, 50)) {
      const geometry = feature['geometry'] as Record<string, unknown> | undefined;
      if (!geometry) continue;

      const geomType = geometry['type'] as string;
      if (geomType !== 'Polygon' && geomType !== 'MultiPolygon') continue;

      const props = (feature['properties'] as Record<string, unknown>) ?? {};
      const name = (props['name'] as string) ?? (props['__name'] as string) ?? `Imported ${autoNameIndex++}`;

      let pointCount = 0;
      try {
        const coords = geometry['coordinates'] as unknown[][][];
        if (geomType === 'Polygon') {
          pointCount = (coords[0] as unknown[]).length;
        } else if (geomType === 'MultiPolygon') {
          pointCount = ((coords as unknown[][][][])[0][0] as unknown[]).length;
        }
        // Subtract 1 for closing vertex if ring is closed
        if (pointCount > 0) pointCount--;
      } catch {
        pointCount = 0;
      }

      let warning: string | undefined;
      if (pointCount < 3) {
        warning = 'Too few points (minimum 3)';
      } else if (pointCount > 500) {
        warning = 'Too many points (maximum 500)';
      }

      previews.push({ name, geometryType: geomType, pointCount, warning });
    }

    if (previews.length === 0) {
      this.parseError.set('No valid Polygon or MultiPolygon features found');
      this.selectedFile.set(null);
      return;
    }

    if (features.length > 50) {
      previews.push({
        name: `...and ${features.length - 50} more (will be skipped)`,
        geometryType: '',
        pointCount: 0,
        warning: 'Exceeds 50-feature import limit',
      });
    }

    this.previewFeatures.set(previews);
  }

  private processFile(file: File): void {
    this.fileError.set(null);
    this.parseError.set(null);

    const validTypes = ['.geojson', '.json'];
    const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
    if (!validTypes.includes(ext)) {
      this.fileError.set('Please select a .geojson or .json file');
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.fileError.set('File size exceeds 5MB limit');
      return;
    }

    this.selectedFile.set(file);

    const reader = new FileReader();
    reader.onload = () => {
      try {
        const json = JSON.parse(reader.result as string);
        this.parseGeoJson(json);
        this.step.set('preview');
      } catch {
        this.parseError.set('Invalid JSON file. Please check the file format.');
        this.selectedFile.set(null);
      }
    };
    reader.onerror = () => {
      this.parseError.set('Failed to read file.');
      this.selectedFile.set(null);
    };
    reader.readAsText(file);
  }
}
