import { DOCUMENT } from '@angular/common';
import { Injectable, OnDestroy, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { take } from 'rxjs';

/**
 * Returns focus to a menu's trigger when a dialog opened from one of its items closes.
 *
 * Material restores focus to whatever held it when the dialog opened. For a dialog opened from a menu
 * item that was the item itself, which is destroyed with the menu, so focus fell to `<body>` and a
 * keyboard user lost their place in the page. Handling it per call site meant every `(click)` handler
 * on every row menu (and the next one someone adds) had to carry a trigger reference; this watches
 * the one thing they share instead.
 *
 * A click on a menu item is seen in the capture phase, before the item's own handler runs. If that
 * handler opens a dialog synchronously, the dialog is tied to the item's trigger. A dialog opened
 * later (after a lazy import, say) needs nothing: by then Material has already put focus back on the
 * trigger, so it is captured correctly.
 */
@Injectable({ providedIn: 'root' })
export class MenuFocusReturnService implements OnDestroy {
  private readonly dialog = inject(MatDialog);
  private readonly document = inject(DOCUMENT);
  private readonly listener = (event: Event) => this.onClick(event);
  private started = false;

  init(): void {
    if (this.started) return;
    this.started = true;
    this.document.addEventListener('click', this.listener, true);
  }

  ngOnDestroy(): void {
    this.document.removeEventListener('click', this.listener, true);
  }

  private onClick(event: Event): void {
    const target = event.target as Element | null;
    const item = target?.closest?.('.mat-mdc-menu-item');
    if (!item) return;
    const trigger = this.triggerFor(item);
    if (!trigger) return;

    const subscription = this.dialog.afterOpened.pipe(take(1)).subscribe(ref => {
      ref
        .afterClosed()
        .pipe(take(1))
        .subscribe(() => {
          if (trigger.isConnected) trigger.focus();
        });
    });
    // The handlers that run after this capture listener are synchronous; anything later is not theirs.
    setTimeout(() => subscription.unsubscribe(), 0);
  }

  /** The element that opened the menu this item belongs to, climbing out of submenus. */
  private triggerFor(item: Element): HTMLElement | null {
    let current: Element | null = item;
    for (let depth = 0; current && depth < 5; depth++) {
      const panel: Element | null = current.closest('.mat-mdc-menu-panel');
      if (!panel?.id) return null;
      const opener: HTMLElement | undefined = Array.from(this.document.querySelectorAll<HTMLElement>('[aria-controls]')).find(
        e => e.getAttribute('aria-controls') === panel.id,
      );
      if (!opener) return null;
      if (!opener.classList.contains('mat-mdc-menu-item')) return opener;
      current = opener;
    }
    return null;
  }
}
