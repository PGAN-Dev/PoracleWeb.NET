import { Injectable, OnDestroy, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';

/**
 * Localises the Material paginator.
 *
 * Without a provided `MatPaginatorIntl` the paginator keeps Material's built-in English — "Items per
 * page:", "1 - 25 of 120", and the English aria-labels on the navigation buttons — so the admin tables
 * stayed partly English in every other locale. See #425.
 *
 * Re-reads its labels whenever the active language changes, because the user can switch language
 * without a reload.
 */
@Injectable()
export class TranslatedPaginatorIntl extends MatPaginatorIntl implements OnDestroy {
  private readonly subscription: Subscription;
  private readonly translate = inject(TranslateService);

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    const total = Math.max(length, 0);

    if (total === 0 || pageSize === 0) {
      return this.translate.instant('PAGINATOR.RANGE_EMPTY', { total });
    }

    const start = page * pageSize;
    // The last page is usually short, and a rounding slip here is visible on every table.
    const end = start < total ? Math.min(start + pageSize, total) : start + pageSize;

    return this.translate.instant('PAGINATOR.RANGE', { end, start: start + 1, total });
  };

  constructor() {
    super();
    this.subscription = this.translate.onLangChange.subscribe(() => this.applyTranslations());
    this.applyTranslations();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  private applyTranslations(): void {
    this.itemsPerPageLabel = this.translate.instant('PAGINATOR.ITEMS_PER_PAGE');
    this.nextPageLabel = this.translate.instant('PAGINATOR.NEXT_PAGE');
    this.previousPageLabel = this.translate.instant('PAGINATOR.PREVIOUS_PAGE');
    this.firstPageLabel = this.translate.instant('PAGINATOR.FIRST_PAGE');
    this.lastPageLabel = this.translate.instant('PAGINATOR.LAST_PAGE');
    this.changes.next();
  }
}
