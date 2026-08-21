import { readFileSync } from 'fs';
import { join } from 'path';

/**
 * A disabled alarm type is read-and-delete: the rules a user already has stay listed and removable,
 * and creating or editing is refused. The API enforces that; these assertions keep the templates
 * honest about it, because the failure mode is silent either way — a create button left visible only
 * fails when someone clicks it, and a delete button accidentally hidden strands alarms that can never
 * fire and can no longer be removed.
 *
 * Read from disk rather than rendered: this is about markup that may not exist yet, on ten pages, and
 * rendering each list component would test the ones already written rather than the next one added.
 */
describe('alarm list templates: read-only when the type is disabled', () => {
  const TEMPLATES = [
    'pokemon/pokemon-list',
    'raids/raid-list',
    'quests/quest-list',
    'invasions/invasion-list',
    'lures/lure-list',
    'nests/nest-list',
    'gyms/gym-list',
    'fort-changes/fort-change-list',
    'max-battles/max-battle-list',
  ];

  // editScope is deliberately absent: the delivery-scope chip stays on the card because it says where
  // the alarm reaches you, which is worth reading while the type is off. Its handler refuses in
  // TypeScript instead, with a message — see the guard assertion at the bottom of this file.
  const WRITE_HANDLERS = [/openAddDialog\(\)/g, /bulkUpdateDistance\(\)/g, /sendTestAlert\(/g, /\(click\)="edit(?!Scope)[A-Z]\w*\(/g];
  const DELETE_HANDLER = /\(click\)="(bulkDelete|delete[A-Z]\w*)\(/;

  const read = (rel: string) => readFileSync(join(__dirname, `${rel}.component.html`), 'utf8');

  /** Spans of every `@if (!writesDisabled()) { … }` block, by brace balance. */
  const guardedBlocks = (html: string): [number, number][] => {
    const spans: [number, number][] = [];
    const opener = /@if \(!writesDisabled\(\)\) \{/g;
    let m: RegExpExecArray | null;
    while ((m = opener.exec(html)) !== null) {
      let depth = 0;
      let i = html.indexOf('{', m.index);
      const start = i;
      for (; i < html.length; i++) {
        if (html[i] === '{') depth++;
        else if (html[i] === '}' && --depth === 0) break;
      }
      spans.push([start, i]);
    }
    return spans;
  };

  const insideGuard = (spans: [number, number][], idx: number) => spans.some(([a, b]) => idx > a && idx < b);

  it.each(TEMPLATES)('%s renders the read-only banner', rel => {
    expect(read(rel)).toContain('<app-feature-readonly-banner');
  });

  it.each(TEMPLATES)('%s guards every create and edit control', rel => {
    const html = read(rel);
    const spans = guardedBlocks(html);
    const unguarded: string[] = [];

    for (const pattern of WRITE_HANDLERS) {
      let m: RegExpExecArray | null;
      const re = new RegExp(pattern.source, 'g');
      while ((m = re.exec(html)) !== null) {
        if (!insideGuard(spans, m.index)) unguarded.push(m[0]);
      }
    }

    expect(unguarded).toEqual([]);
  });

  it.each(TEMPLATES)('%s refuses scope edits in code, since the chip itself stays readable', rel => {
    const ts = readFileSync(join(__dirname, `${rel}.component.ts`), 'utf8');
    const handler = ts.slice(ts.indexOf('editScope('));

    expect(handler).toContain('if (this.writesDisabled())');
    expect(handler.slice(0, handler.indexOf('}'))).toContain('READ_ONLY_TOAST');
  });

  it.each(TEMPLATES)('%s leaves deleting alone', rel => {
    const html = read(rel);
    const hidden = guardedBlocks(html)
      .map(([a, b]) => html.slice(a, b))
      .filter(block => DELETE_HANDLER.test(block))
      .map(block => DELETE_HANDLER.exec(block)?.[1]);

    expect(hidden).toEqual([]);
  });
});
