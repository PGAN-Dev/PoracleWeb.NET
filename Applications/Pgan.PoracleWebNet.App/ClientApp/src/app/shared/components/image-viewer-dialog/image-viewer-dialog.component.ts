import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

export interface ImageViewerDialogData {
  /** Accessible description of the image; also shown as the caption. */
  alt: string;
  src: string;
}

/** Full-size viewer for the help screenshots, which are downscaled to the width of the help column. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-image-viewer-dialog',
  standalone: true,
  styleUrl: './image-viewer-dialog.component.scss',
  templateUrl: './image-viewer-dialog.component.html',
})
export class ImageViewerDialogComponent {
  readonly data = inject<ImageViewerDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ImageViewerDialogComponent>);

  close(): void {
    this.dialogRef.close();
  }
}
