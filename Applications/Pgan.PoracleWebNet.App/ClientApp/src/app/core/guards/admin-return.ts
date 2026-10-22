/**
 * Navigation state for the app's own trips back to /admin when an impersonation ends. The account that
 * lands there may be a delegate's rather than an admin's, and being bounced from a page they never asked
 * for needs no explanation, so `adminGuard` skips its "That page is for admins" toast.
 */
export const ADMIN_RETURN_KEY = 'adminReturn';
export const ADMIN_RETURN_STATE = { [ADMIN_RETURN_KEY]: true };
