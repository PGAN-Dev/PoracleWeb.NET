import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The add and edit dialogs must offer the same PVP controls.
 *
 * The mega evolution picker was added to both, but in the edit dialog it was nested *inside* the
 * level-cap fieldset, which renders only when the server advertises PVP level caps. On a server with
 * none — the common case, and the one this deployment runs — the add dialog offered the picker and the
 * edit dialog silently did not, so a mega rule could be created and then never changed.
 *
 * Asserted against the templates rather than the DOM: Material keeps only the active tab body mounted,
 * and driving a tab switch in this zoneless harness costs more machinery than the assertion is worth.
 * The defect was structural — a control in the wrong block — so the structure is what this checks.
 */
describe('PVP controls, add dialog versus edit dialog', () => {
  const read = (name: string) => readFileSync(join(__dirname, name), 'utf8');

  const templates = {
    add: read('pokemon-add-dialog.component.html'),
    edit: read('pokemon-edit-dialog.component.html'),
  };

  /** The body of the `@if (showCapPicker())` block, which only exists on a caps-advertising server. */
  function capPickerBlock(template: string): string {
    const start = template.indexOf('@if (showCapPicker()) {');
    if (start === -1) return '';

    // Prettier keeps the closing brace at the opening line's indent, so that is the block's end.
    const indent = ' '.repeat(template.slice(0, start).length - template.lastIndexOf('\n', start) - 1);
    const end = template.indexOf(`\n${indent}}`, start);
    return template.slice(start, end === -1 ? undefined : end);
  }

  it.each(Object.entries(templates))('%s offers the mega evolution picker', (_name, template) => {
    expect(template).toContain('POKEMON.PVP_EVOLUTION');
  });

  it.each(Object.entries(templates))('%s does not hide it behind the level-cap picker', (_name, template) => {
    expect(capPickerBlock(template)).not.toContain('POKEMON.PVP_EVOLUTION');
  });

  it('has a cap-picker block to be outside of, in the edit dialog', () => {
    // Guards the guard: if the block is renamed, capPickerBlock returns '' and the test above passes
    // for the wrong reason.
    expect(capPickerBlock(templates.edit)).toContain('POKEMON.PVP_CAP');
  });
});
