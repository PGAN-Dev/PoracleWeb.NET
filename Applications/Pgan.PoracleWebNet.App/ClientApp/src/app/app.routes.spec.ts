import { routes } from './app.routes';

describe('app routes', () => {
  it('sends the old webhooks address to destinations', () => {
    const old = routes.find(r => r.path === 'admin/webhooks');
    expect(old?.redirectTo).toBe('admin/destinations');
    expect(old?.pathMatch).toBe('full');
  });

  it('serves the destinations page behind the admin guards', () => {
    const page = routes.find(r => r.path === 'admin/destinations');
    expect(page?.title).toBe('NAV.DESTINATIONS');
    expect(page?.canActivate?.length).toBe(2);
  });

  it('redirects /my-webhooks', () => {
    // A delegate's bookmark, and the exit route an impersonation session started before the rename recorded.
    const old = routes.find(r => r.path === 'my-webhooks');
    expect(old?.redirectTo).toBe('my-destinations');
    expect(old?.pathMatch).toBe('full');
  });

  it('serves My Destinations to any signed-in user', () => {
    const page = routes.find(r => r.path === 'my-destinations');
    expect(page?.title).toBe('NAV.MY_DESTINATIONS');
    expect(page?.canActivate?.length).toBe(1);
  });
});
