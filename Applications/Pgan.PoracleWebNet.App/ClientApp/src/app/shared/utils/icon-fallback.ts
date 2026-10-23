/**
 * Stand-ins for artwork an icon pack doesn't carry. The default UICONS pack has no `invasion/46.png`
 * (Decoy Grunt), so the picture 404'd and the slot was hidden or drew the browser's broken-image mark.
 * Inline SVG, so the fallback itself can't fail; the CSP allows `data:` images.
 */
export const GENERIC_GRUNT_ICON =
  "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z'/%3E%3C/svg%3E";

/** Swaps a failed image for the fallback, once: a fallback that fails too is left alone rather than looped. */
export function swapToFallbackIcon(event: Event, fallback: string = GENERIC_GRUNT_ICON): void {
  const img = event.target as HTMLImageElement | null;
  if (!img || img.src === fallback) return;
  img.src = fallback;
}
