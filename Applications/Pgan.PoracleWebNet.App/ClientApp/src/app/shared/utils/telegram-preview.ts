/**
 * A small renderer for the text of a Telegram DTS template, for the template preview (#1027).
 *
 * It turns `content` plus `parse_mode` into a tree of nodes the template draws as real elements. Nothing here
 * produces HTML: the template binds `text` through interpolation, so a template that holds `<script>` shows it
 * as text, and the only attribute taken from template text is a link's `href`, which `safeTelegramUrl` limits
 * to http, https and tg.
 *
 * The parsers follow Telegram's own rules loosely. Anything they can't make sense of (an unclosed marker, an
 * unknown tag) stays as literal text, so a template is never shown as nothing. Telegram would refuse some of
 * those messages outright; the preview shows them rather than guessing.
 */

export type TgKind = 'a' | 'b' | 'code' | 'i' | 'pre' | 's' | 'spoiler' | 'text' | 'u';

export interface TgNode {
  children?: TgNode[];
  /** Only on `a`. Null when the address isn't one the preview will link to. */
  href?: null | string;
  kind: TgKind;
  /** Only on `text`. */
  text?: string;
}

export type TgParseMode = 'HTML' | 'Markdown' | 'MarkdownV2';

/** Past this a template isn't a message Telegram would send (its limit is 4096), so it is shown as plain text. */
const MAX_SOURCE_LENGTH = 20000;
const MAX_DEPTH = 8;

/** The mode PoracleNG sends with: it is case-insensitive and an empty one means Markdown. Null is "can't render". */
export function normalizeParseMode(mode: null | string | undefined): null | TgParseMode {
  switch ((mode ?? '').toLowerCase()) {
    case '':
    case 'markdown':
      return 'Markdown';
    case 'markdownv2':
      return 'MarkdownV2';
    case 'html':
      return 'HTML';
    default:
      return null;
  }
}

/** A link target the preview will make clickable. Everything else (javascript:, data:, '#') is not one. */
export function safeTelegramUrl(url: null | string | undefined): null | string {
  const trimmed = (url ?? '').trim();
  return /^(?:https?|tg):\/\/\S+$/i.test(trimmed) ? trimmed : null;
}

export function plainText(nodes: TgNode[]): string {
  return nodes.map(n => (n.kind === 'text' ? (n.text ?? '') : plainText(n.children ?? []))).join('');
}

/** Never throws. A null mode, or one that fails, answers the text as a single plain node. */
export function parseTelegram(text: string, mode: null | TgParseMode): TgNode[] {
  if (!text) return [];
  const plain: TgNode[] = [{ kind: 'text', text }];
  if (mode === null || text.length > MAX_SOURCE_LENGTH) return plain;
  try {
    return mode === 'HTML' ? parseHtml(text) : parseMarkdown(text, mode === 'MarkdownV2', 0);
  } catch {
    return plain;
  }
}

function findClose(src: string, marker: string, from: number): number {
  let j = from;
  while (j < src.length) {
    if (src[j] === '\\') {
      j += 2;
      continue;
    }
    if (src.startsWith(marker, j)) return j;
    j++;
  }
  return -1;
}

function parseMarkdown(src: string, v2: boolean, depth: number): TgNode[] {
  if (depth > MAX_DEPTH) return [{ kind: 'text', text: src }];

  const out: TgNode[] = [];
  let buf = '';
  const flush = (): void => {
    if (buf) out.push({ kind: 'text', text: buf });
    buf = '';
  };
  const escapable = v2 ? /[!-/:-@[-`{-~]/ : /[_*`[]/;
  const markers: [string, TgKind][] = v2
    ? [
        ['||', 'spoiler'],
        ['__', 'u'],
        ['*', 'b'],
        ['_', 'i'],
        ['~', 's'],
      ]
    : [
        // Telegram's legacy Markdown has no `**`, but the shipped fallback templates use it as bold.
        ['**', 'b'],
        ['*', 'b'],
        ['_', 'i'],
      ];

  let i = 0;
  while (i < src.length) {
    const c = src[i];

    if (c === '\\' && i + 1 < src.length && escapable.test(src[i + 1])) {
      buf += src[i + 1];
      i += 2;
      continue;
    }

    if (src.startsWith('```', i)) {
      const end = src.indexOf('```', i + 3);
      if (end > 0) {
        flush();
        out.push({ children: [{ kind: 'text', text: src.slice(i + 3, end).replace(/^[\w+#.-]*\n/, '') }], kind: 'pre' });
        i = end + 3;
      } else {
        buf += '```';
        i += 3;
      }
      continue;
    }

    if (c === '`') {
      const end = src.indexOf('`', i + 1);
      if (end > i + 1) {
        flush();
        out.push({ children: [{ kind: 'text', text: src.slice(i + 1, end) }], kind: 'code' });
        i = end + 1;
        continue;
      }
    }

    if (c === '[') {
      const mid = src.indexOf('](', i + 1);
      const end = mid > 0 ? src.indexOf(')', mid + 2) : -1;
      if (mid > i + 1 && end > 0) {
        flush();
        out.push({
          children: parseMarkdown(src.slice(i + 1, mid), v2, depth + 1),
          href: safeTelegramUrl(src.slice(mid + 2, end)),
          kind: 'a',
        });
        i = end + 1;
        continue;
      }
    }

    const marker = markers.find(([m]) => src.startsWith(m, i));
    if (marker) {
      const [m, kind] = marker;
      const close = findClose(src, m, i + m.length);
      if (close > i + m.length) {
        flush();
        out.push({ children: parseMarkdown(src.slice(i + m.length, close), v2, depth + 1), kind });
        i = close + m.length;
      } else {
        buf += m;
        i += m.length;
      }
      continue;
    }

    buf += c;
    i++;
  }
  flush();
  return out;
}

const HTML_TAG_KINDS: Record<string, TgKind> = {
  a: 'a',
  b: 'b',
  code: 'code',
  del: 's',
  em: 'i',
  i: 'i',
  ins: 'u',
  pre: 'pre',
  s: 's',
  strike: 's',
  strong: 'b',
  'tg-spoiler': 'spoiler',
  u: 'u',
};

function decodeEntities(text: string): string {
  return text.replace(/&(lt|gt|amp|quot|#\d+|#x[0-9a-f]+);/gi, (whole, body: string) => {
    switch (body.toLowerCase()) {
      case 'lt':
        return '<';
      case 'gt':
        return '>';
      case 'amp':
        return '&';
      case 'quot':
        return '"';
    }
    const code = body[1].toLowerCase() === 'x' ? parseInt(body.slice(2), 16) : parseInt(body.slice(1), 10);
    return code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : whole;
  });
}

function parseHtml(src: string): TgNode[] {
  const root: TgNode[] = [];
  const stack: { node: TgNode; tag: string }[] = [];
  const target = (): TgNode[] => (stack.length ? (stack[stack.length - 1].node.children as TgNode[]) : root);
  const addText = (text: string): void => {
    const list = target();
    const last = list[list.length - 1];
    if (last && last.kind === 'text') last.text += text;
    else list.push({ kind: 'text', text });
  };

  const token = /<(\/?)([a-zA-Z][a-zA-Z0-9-]*)((?:\s[^<>]*)?)>|[^<]+|</g;
  let m: null | RegExpExecArray;
  while ((m = token.exec(src)) !== null) {
    if (m[2] === undefined) {
      addText(m[0] === '<' ? '<' : decodeEntities(m[0]));
      continue;
    }
    const tag = m[2].toLowerCase();
    if (m[1]) {
      const at = stack.map(s => s.tag).lastIndexOf(tag);
      if (at >= 0) stack.length = at;
      else addText(m[0]);
      continue;
    }
    const spoilerSpan = tag === 'span' && /\sclass\s*=\s*["']tg-spoiler["']/i.test(m[3]);
    const kind = spoilerSpan ? 'spoiler' : HTML_TAG_KINDS[tag];
    if (!kind) {
      addText(m[0]);
      continue;
    }
    const node: TgNode = { children: [], kind };
    if (kind === 'a') {
      const href = /\shref\s*=\s*(?:"([^"]*)"|'([^']*)')/i.exec(m[3]);
      node.href = safeTelegramUrl(decodeEntities(href?.[1] ?? href?.[2] ?? ''));
    }
    target().push(node);
    stack.push({ node, tag });
  }
  return root;
}
