// Dev server runs Angular at :4200 with a proxy (see proxy.conf.json) that
// forwards /api/* and /auth/* to the local API on :8082. Empty `apiUrl` means
// all HTTP calls become same-origin from the browser's view — identical to
// the production single-port setup.
export const environment = {
  apiUrl: '',
  production: false,
};
