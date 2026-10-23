import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A two-line confirm ("Delete 2 alarms? This can't be undone.") took the whole phone screen under the
 * fullscreen dialog rules, with its buttons 800px below the question (UI audit N8). On a phone it rises
 * from the bottom at its own height, the way the where sheet does, whichever of the fifty-odd call sites
 * opened it: the rules key on the component inside the pane, not on a panel class each caller must pass.
 */
describe('confirm dialog on a phone', () => {
  const styles = readFileSync(join(__dirname, '../../../../styles.scss'), 'utf8');
  const start = styles.indexOf('// ─── Phone bottom sheet');
  const section = styles.slice(start, styles.indexOf('// ───', start + 10));

  it('anchors the overlay to the bottom of the screen', () => {
    expect(section).toContain('.cdk-global-overlay-wrapper:has(> .cdk-overlay-pane app-confirm-dialog)');
  });

  it('sizes the pane and the surface to their content', () => {
    expect(section).toContain('.cdk-overlay-pane:has(app-confirm-dialog)');
    expect(section).toContain('.cdk-overlay-pane:has(app-confirm-dialog) mat-dialog-container .mat-mdc-dialog-surface');
  });

  it('still keeps the where sheet a bottom sheet', () => {
    expect(section).toContain('.cdk-global-overlay-wrapper:has(> .phone-bottom-sheet)');
    expect(section).toContain('.cdk-overlay-pane.phone-bottom-sheet');
  });
});
