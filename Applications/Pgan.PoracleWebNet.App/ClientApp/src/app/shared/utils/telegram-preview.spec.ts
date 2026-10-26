import { TgNode, normalizeParseMode, parseTelegram, plainText, safeTelegramUrl } from './telegram-preview';

const kinds = (nodes: TgNode[]): string[] => nodes.map(n => n.kind);

describe('normalizeParseMode', () => {
  it('defaults to Markdown the way PoracleNG does', () => {
    expect(normalizeParseMode(undefined)).toBe('Markdown');
    expect(normalizeParseMode('')).toBe('Markdown');
  });

  it('is case-insensitive', () => {
    expect(normalizeParseMode('markdown')).toBe('Markdown');
    expect(normalizeParseMode('MARKDOWNV2')).toBe('MarkdownV2');
    expect(normalizeParseMode('html')).toBe('HTML');
  });

  it('answers null for a mode it cannot render', () => {
    expect(normalizeParseMode('BBCode')).toBeNull();
  });
});

describe('safeTelegramUrl', () => {
  it('allows http, https and tg links', () => {
    expect(safeTelegramUrl('https://example.com/a?b=1')).toBe('https://example.com/a?b=1');
    expect(safeTelegramUrl(' http://example.com ')).toBe('http://example.com');
    expect(safeTelegramUrl('tg://user?id=1')).toBe('tg://user?id=1');
  });

  it('refuses anything that can run script or is not a link', () => {
    expect(safeTelegramUrl('javascript:alert(1)')).toBeNull();
    expect(safeTelegramUrl(' JaVaScRiPt:alert(1)')).toBeNull();
    expect(safeTelegramUrl('data:text/html,<script>alert(1)</script>')).toBeNull();
    expect(safeTelegramUrl('vbscript:x')).toBeNull();
    expect(safeTelegramUrl('#')).toBeNull();
    expect(safeTelegramUrl('')).toBeNull();
  });
});

describe('parseTelegram: Markdown', () => {
  it('renders bold, italic and code', () => {
    const nodes = parseTelegram('*Pikachu* is _here_ at `12:30`', 'Markdown');
    expect(kinds(nodes)).toEqual(['b', 'text', 'i', 'text', 'code']);
    expect(plainText(nodes)).toBe('Pikachu is here at 12:30');
  });

  it('reads the ** the fallback templates use as bold', () => {
    const nodes = parseTelegram('**Great league:**\nx', 'Markdown');
    expect(nodes[0].kind).toBe('b');
    expect(plainText(nodes[0].children ?? [])).toBe('Great league:');
  });

  it('renders a link and keeps its text', () => {
    const [link] = parseTelegram('[Google](https://maps.google.com/?q=1)', 'Markdown');
    expect(link.kind).toBe('a');
    expect(link.href).toBe('https://maps.google.com/?q=1');
    expect(plainText(link.children ?? [])).toBe('Google');
  });

  it('keeps a link whose address is not usable as text, without an href', () => {
    const [link] = parseTelegram('[Google](#)', 'Markdown');
    expect(link.kind).toBe('a');
    expect(link.href).toBeNull();
  });

  it('renders a fenced block as pre and drops a language line', () => {
    const [pre] = parseTelegram('```json\n{"a":1}\n```', 'Markdown');
    expect(pre.kind).toBe('pre');
    expect(plainText([pre])).toBe('{"a":1}\n');
  });

  it('leaves an unclosed marker as literal text', () => {
    const nodes = parseTelegram('5 * 3 and snake_case', 'Markdown');
    expect(plainText(nodes)).toBe('5 * 3 and snake_case');
  });

  it('nests formatting inside a link', () => {
    const [link] = parseTelegram('[*Map*](https://x.test)', 'Markdown');
    expect(link.kind).toBe('a');
    expect(kinds(link.children ?? [])).toEqual(['b']);
  });

  it('honours a backslash escape', () => {
    expect(plainText(parseTelegram('\\*not bold\\*', 'Markdown'))).toBe('*not bold*');
  });
});

describe('parseTelegram: MarkdownV2', () => {
  it('renders underline, strike and spoiler as well', () => {
    const nodes = parseTelegram('__under__ ~gone~ ||secret|| *b* _i_', 'MarkdownV2');
    expect(kinds(nodes).filter(k => k !== 'text')).toEqual(['u', 's', 'spoiler', 'b', 'i']);
  });

  it('unescapes reserved characters', () => {
    expect(plainText(parseTelegram('1\\. Hello\\!', 'MarkdownV2'))).toBe('1. Hello!');
  });
});

describe('parseTelegram: HTML', () => {
  it('renders the tags Telegram supports', () => {
    const nodes = parseTelegram('<b>a</b><strong>b</strong><i>c</i><u>d</u><s>e</s><code>f</code><tg-spoiler>g</tg-spoiler>', 'HTML');
    expect(kinds(nodes)).toEqual(['b', 'b', 'i', 'u', 's', 'code', 'spoiler']);
  });

  it('renders a link and decodes entities', () => {
    const [link, text] = parseTelegram('<a href="https://x.test/?a=1&amp;b=2">go</a> 1 &lt; 2', 'HTML');
    expect(link.href).toBe('https://x.test/?a=1&b=2');
    expect(plainText([text])).toBe(' 1 < 2');
  });

  it('shows a tag it does not know as text instead of dropping it', () => {
    const nodes = parseTelegram('<blink>hi</blink>', 'HTML');
    expect(plainText(nodes)).toBe('<blink>hi</blink>');
  });

  it('closes a tag that was never closed', () => {
    const nodes = parseTelegram('<b>open', 'HTML');
    expect(kinds(nodes)).toEqual(['b']);
    expect(plainText(nodes)).toBe('open');
  });

  it('keeps a stray closing tag as text', () => {
    expect(plainText(parseTelegram('a</b>b', 'HTML'))).toBe('a</b>b');
  });
});

describe('parseTelegram: hostile and odd input', () => {
  it('never produces a node for a script tag, in any mode', () => {
    for (const mode of ['Markdown', 'MarkdownV2', 'HTML'] as const) {
      const nodes = parseTelegram('<script>alert(1)</script><img src=x onerror=alert(1)>', mode);
      expect(plainText(nodes)).toContain('<script>');
      expect(nodes.every(n => ['text'].includes(n.kind))).toBe(true);
    }
  });

  it('gives a javascript: link no href in any mode', () => {
    const md = parseTelegram('[x](javascript:alert(1))', 'Markdown')[0];
    const html = parseTelegram('<a href="javascript:alert(1)">x</a>', 'HTML')[0];
    const html2 = parseTelegram("<a href='  JAVASCRIPT:alert(1)'>x</a>", 'HTML')[0];
    expect(md.href).toBeNull();
    expect(html.href).toBeNull();
    expect(html2.href).toBeNull();
  });

  it('returns the text as it is for a parse mode it cannot render', () => {
    expect(parseTelegram('*a* <b>c</b>', null)).toEqual([{ kind: 'text', text: '*a* <b>c</b>' }]);
  });

  it('returns nothing for empty input', () => {
    expect(parseTelegram('', 'Markdown')).toEqual([]);
  });

  it('survives a long run of unclosed markers', () => {
    const nodes = parseTelegram('*'.repeat(3000) + '_'.repeat(3000), 'MarkdownV2');
    expect(plainText(nodes).length).toBeGreaterThan(0);
  });
});
