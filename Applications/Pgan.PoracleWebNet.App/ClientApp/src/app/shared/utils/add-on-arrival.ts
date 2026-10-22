import { inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

/**
 * `/pokemon?add=1`: the list opens its add dialog on arrival. The dashboard's "Add Pokémon" quick
 * actions, its first-alarm tip and the onboarding wizard's alarm step all said "Add" and only ever
 * landed on the list, so the user had to find the button a second time.
 */
export const ADD_QUERY_PARAM = 'add';

/** Query params that ask a list to open its add dialog. */
export const ADD_ON_ARRIVAL = { [ADD_QUERY_PARAM]: 1 } as const;

/**
 * Call from a field initializer; call what it returns from `ngOnInit`. Answers true once when the URL
 * asked for the add dialog, and takes `add` out of the URL with `replaceUrl`, so Back, a reload or a
 * bookmark of the list does not open the dialog again.
 *
 * Both injections are optional, so a list rendered in a test without a router simply never opens it.
 */
export function addOnArrival(): () => boolean {
  const route = inject(ActivatedRoute, { optional: true });
  const router = inject(Router, { optional: true });

  return () => {
    if (!route || !router || !route.snapshot.queryParamMap.has(ADD_QUERY_PARAM)) return false;
    void router.navigate([], {
      queryParams: { [ADD_QUERY_PARAM]: null },
      queryParamsHandling: 'merge',
      relativeTo: route,
      replaceUrl: true,
    });
    return true;
  };
}
