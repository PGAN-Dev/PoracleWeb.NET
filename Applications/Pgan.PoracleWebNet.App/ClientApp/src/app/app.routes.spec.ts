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
});
