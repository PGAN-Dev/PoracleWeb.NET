/**
 * A webhook URL with its secret token cut off: the host and, for a Discord-style URL, the webhook number.
 * Anything after that number, and the whole path of any other URL, is dropped rather than shortened,
 * since the token is usually the last segment. The full URL stays reachable only through
 * Copy ID on Destinations. Shared with My Destinations so the two pages hide the same part.
 */
export function shortWebhookUrl(url: string): string {
  let parsed: URL;
  try {
    parsed = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(url) ? url : `https://${url}`);
  } catch {
    return '…';
  }
  if (!parsed.host || /\s/.test(url)) return '…';
  const segments = parsed.pathname.split('/').filter(Boolean);
  const hooks = segments.findIndex(s => s.toLowerCase() === 'webhooks');
  if (hooks >= 0 && /^\d+$/.test(segments[hooks + 1] ?? '')) {
    return `${parsed.host}/${segments.slice(0, hooks + 2).join('/')}…`;
  }
  return segments.length ? `${parsed.host}/…` : parsed.host;
}
