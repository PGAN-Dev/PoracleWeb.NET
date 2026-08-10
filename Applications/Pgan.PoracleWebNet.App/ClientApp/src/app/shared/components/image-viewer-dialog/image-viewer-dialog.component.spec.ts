import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import { ImageViewerDialogComponent, ImageViewerDialogData } from './image-viewer-dialog.component';

describe('ImageViewerDialogComponent', () => {
  let dialogRef: { close: jest.Mock };

  function setup(data: ImageViewerDialogData) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: dialogRef }],
      imports: [ImageViewerDialogComponent],
    });

    const fixture = TestBed.createComponent(ImageViewerDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the image at the given source with its alt text', () => {
    const fixture = setup({ alt: 'Dashboard overview', src: 'assets/help/dashboard-overview.png' });

    const img = fixture.nativeElement.querySelector('img.viewer-image') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('assets/help/dashboard-overview.png');
    expect(img.getAttribute('alt')).toBe('Dashboard overview');
  });

  it('shows the alt text as a caption', () => {
    const fixture = setup({ alt: 'Dashboard overview', src: 'assets/help/dashboard-overview.png' });

    expect((fixture.nativeElement.querySelector('.viewer-caption') as HTMLElement).textContent).toContain('Dashboard overview');
  });

  it('omits the caption when there is no alt text', () => {
    const fixture = setup({ alt: '', src: 'assets/help/dashboard-overview.png' });

    expect(fixture.nativeElement.querySelector('.viewer-caption')).toBeNull();
  });

  it('closes when the image is clicked', () => {
    const fixture = setup({ alt: 'Dashboard overview', src: 'assets/help/dashboard-overview.png' });

    (fixture.nativeElement.querySelector('img.viewer-image') as HTMLImageElement).click();

    expect(dialogRef.close).toHaveBeenCalled();
  });

  it('closes when the close button is clicked', () => {
    const fixture = setup({ alt: 'Dashboard overview', src: 'assets/help/dashboard-overview.png' });

    (fixture.nativeElement.querySelector('.viewer-close') as HTMLButtonElement).click();

    expect(dialogRef.close).toHaveBeenCalled();
  });
});
