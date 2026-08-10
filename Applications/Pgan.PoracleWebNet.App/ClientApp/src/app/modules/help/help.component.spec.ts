import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';

import { HelpComponent } from './help.component';
import { ImageViewerDialogComponent } from '../../shared/components/image-viewer-dialog/image-viewer-dialog.component';

describe('HelpComponent screenshots', () => {
  let fixture: ComponentFixture<HelpComponent>;
  let dialog: { open: jest.Mock };

  const SHOT = '<img class="help-screenshot" src="assets/help/dashboard-overview.png" alt="Dashboard overview" /><p>Body copy.</p>';

  beforeEach(() => {
    dialog = { open: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), { provide: MatDialog, useValue: dialog }],
      imports: [HelpComponent, NoopAnimationsModule],
    });

    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { HELP: { CONTENT_DASHBOARD: SHOT, IMAGE_ENLARGE: 'Click to enlarge' } }, true);
    translate.use('en');

    fixture = TestBed.createComponent(HelpComponent);
    fixture.detectChanges();
  });

  function screenshot(): HTMLImageElement {
    const img = fixture.nativeElement.querySelector('img.help-screenshot') as HTMLImageElement | null;
    expect(img).not.toBeNull();
    return img as HTMLImageElement;
  }

  it('marks injected screenshots as focusable buttons', () => {
    const img = screenshot();

    expect(img.tabIndex).toBe(0);
    expect(img.getAttribute('role')).toBe('button');
    expect(img.getAttribute('aria-label')).toBe('Dashboard overview — Click to enlarge');
  });

  it('opens the viewer when a screenshot is clicked', () => {
    screenshot().click();

    expect(dialog.open).toHaveBeenCalledWith(
      ImageViewerDialogComponent,
      expect.objectContaining({
        data: { alt: 'Dashboard overview', src: expect.stringContaining('assets/help/dashboard-overview.png') },
      }),
    );
  });

  it('opens the viewer on Enter and Space', () => {
    const img = screenshot();

    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter' }));
    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: ' ' }));

    expect(dialog.open).toHaveBeenCalledTimes(2);
  });

  it('ignores other keys and clicks on surrounding prose', () => {
    const img = screenshot();
    img.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'a' }));
    (fixture.nativeElement.querySelector('.section-content p') as HTMLElement).click();

    expect(dialog.open).not.toHaveBeenCalled();
  });
});
